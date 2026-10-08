using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using QRCoder;

namespace DepEdDTRSystem.Services;

public sealed class DtrReportVerificationService
{
    private const int TokenVersion = 1;
    private const int FingerprintLength = 16;
    private const int PayloadLength = 1 + sizeof(int) + 1 + sizeof(ushort) + FingerprintLength + sizeof(long);
    private readonly IDataProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public DtrReportVerificationService(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _protector = dataProtectionProvider.CreateProtector("DepEdDTRSystem.Form48Verification.v1");
        _configuration = configuration;
        _environment = environment;
    }

    public void PrepareForVerification(DtrForm48ViewModel report, HttpRequest request)
    {
        var payload = new DtrVerificationPayload(
            TokenVersion,
            report.EmployeeDatabaseId,
            report.Month,
            report.Year,
            CreateFingerprint(report)[..(FingerprintLength * 2)],
            DateTimeOffset.UtcNow);
        var token = _protector.Protect(EncodePayload(payload));
        var verificationUrl = $"{GetPublicBaseUrl(request)}/DtrVerification?token={Uri.EscapeDataString(token)}";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(verificationUrl, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(qrData).GetGraphic(10);

        report.VerificationUrl = verificationUrl;
        report.VerificationQrCodeDataUri = $"data:image/png;base64,{Convert.ToBase64String(png)}";
    }

    public bool TryReadToken(string? token, out DtrVerificationPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        try
        {
            var encodedPayload = _protector.Unprotect(token);
            var candidate = DecodePayload(encodedPayload);
            if (candidate is null
                || candidate.Version != TokenVersion
                || candidate.EmployeeId <= 0
                || candidate.Month is < 1 or > 12
                || candidate.Year is < 2000 or > 9998
                || candidate.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5)
                || !IsFingerprint(candidate.Fingerprint))
            {
                return false;
            }

            payload = candidate;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool MatchesCurrentReport(DtrVerificationPayload payload, DtrForm48ViewModel report)
    {
        if (payload.EmployeeId != report.EmployeeDatabaseId
            || payload.Month != report.Month
            || payload.Year != report.Year
            || !IsFingerprint(payload.Fingerprint))
        {
            return false;
        }

        var expected = Convert.FromHexString(payload.Fingerprint);
        var actual = Convert.FromHexString(CreateFingerprint(report))[..expected.Length];
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string GetPublicBaseUrl(HttpRequest request)
    {
        var configuredUrl = _configuration["App:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (!_environment.IsDevelopment() || !request.Host.HasValue)
            {
                throw new InvalidOperationException(
                    "App:PublicBaseUrl must be configured to generate DTR verification QR codes.");
            }

            configuredUrl = $"{request.Scheme}://{request.Host}";
        }

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Query.Length != 0
            || baseUri.Fragment.Length != 0
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && !(_environment.IsDevelopment() && baseUri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new InvalidOperationException(
                "App:PublicBaseUrl must be an absolute HTTPS URL (HTTP is allowed only in development).");
        }

        return baseUri.AbsoluteUri.TrimEnd('/');
    }

    private static string CreateFingerprint(DtrForm48ViewModel report)
    {
        var data = new DtrFingerprint(
            report.EmployeeDatabaseId,
            report.EmployeeName,
            report.EmployeeNumber,
            report.Position,
            report.SchoolName,
            report.SchoolId,
            report.SchoolHeadName,
            report.SchoolHeadPosition,
            report.Month,
            report.Year,
            report.ApprovedLeaves.Select(leave => new DtrLeaveFingerprint(
                leave.LeaveType,
                leave.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                leave.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                leave.NumberOfDays)).ToArray(),
            report.ApprovedSeminars.Select(seminar => new DtrSeminarFingerprint(
                seminar.SeminarTitle,
                seminar.Organizer,
                seminar.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                seminar.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                seminar.Venue,
                seminar.Purpose)).ToArray(),
            report.Days.Select(day => new DtrDayFingerprint(
                day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.AmTimeIn,
                day.AmTimeOut,
                day.PmTimeIn,
                day.PmTimeOut,
                day.OnLeaveLabel,
                day.OnSeminarLabel,
                day.UndertimeMinutes)).ToArray());

        var json = JsonSerializer.Serialize(data);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..(FingerprintLength * 2)];
    }

    private static bool IsFingerprint(string? value) =>
        value is { Length: FingerprintLength * 2 or 64 } && value.All(Uri.IsHexDigit);

    private static string EncodePayload(DtrVerificationPayload payload)
    {
        var bytes = new byte[PayloadLength];
        var offset = 0;
        bytes[offset++] = checked((byte)payload.Version);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), payload.EmployeeId);
        offset += sizeof(int);
        bytes[offset++] = checked((byte)payload.Month);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), checked((ushort)payload.Year));
        offset += sizeof(ushort);
        Convert.FromHexString(payload.Fingerprint).CopyTo(bytes, offset);
        offset += FingerprintLength;
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset), payload.IssuedAtUtc.ToUnixTimeSeconds());

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static DtrVerificationPayload? DecodePayload(string encodedPayload)
    {
        if (encodedPayload.StartsWith('{'))
        {
            return JsonSerializer.Deserialize<DtrVerificationPayload>(encodedPayload);
        }

        var base64 = encodedPayload.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length != PayloadLength)
        {
            return null;
        }

        var offset = 0;
        var version = bytes[offset++];
        var employeeId = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
        offset += sizeof(int);
        var month = bytes[offset++];
        var year = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
        offset += sizeof(ushort);
        var fingerprint = Convert.ToHexString(bytes.AsSpan(offset, FingerprintLength));
        offset += FingerprintLength;
        var issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(offset)));

        return new DtrVerificationPayload(version, employeeId, month, year, fingerprint, issuedAtUtc);
    }

    private sealed record DtrFingerprint(
        int EmployeeId,
        string EmployeeName,
        string EmployeeNumber,
        string Position,
        string SchoolName,
        string SchoolId,
        string SchoolHeadName,
        string SchoolHeadPosition,
        int Month,
        int Year,
        IReadOnlyList<DtrLeaveFingerprint> ApprovedLeaves,
        IReadOnlyList<DtrSeminarFingerprint> ApprovedSeminars,
        IReadOnlyList<DtrDayFingerprint> Days);

    private sealed record DtrLeaveFingerprint(
        string LeaveType,
        string StartDate,
        string EndDate,
        decimal NumberOfDays);

    private sealed record DtrSeminarFingerprint(
        string SeminarTitle,
        string Organizer,
        string StartDate,
        string EndDate,
        string Venue,
        string Purpose);

    private sealed record DtrDayFingerprint(
        string Date,
        string AmTimeIn,
        string AmTimeOut,
        string PmTimeIn,
        string PmTimeOut,
        string OnLeaveLabel,
        string OnSeminarLabel,
        int UndertimeMinutes);
}

public sealed record DtrVerificationPayload(
    int Version,
    int EmployeeId,
    int Month,
    int Year,
    string Fingerprint,
    DateTimeOffset IssuedAtUtc);
