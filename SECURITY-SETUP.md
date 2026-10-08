# Admin security setup

Admin credentials and mail-delivery secrets must not be committed to source control.
For local development, use .NET user-secrets from the project directory. In production,
provide the same configuration keys through a secret manager or environment variables.

## Initialize the first admin account

Before starting the app for the first time after applying the `AddAdminAccounts`
migration, set the admin username, initial password, and the mailbox that will receive
password-reset links:

```powershell
dotnet user-secrets set "Admin:Username" "admin"
dotnet user-secrets set "Admin:Password" "<a unique, strong initial password>"
dotnet user-secrets set "Admin:Email" "<admin mailbox>"
```

The app hashes the password with ASP.NET Core's password hasher and stores the account
in the database. The configured password is used only to initialize an empty admin
account; it does not overwrite an existing account. Remove `Admin:Password` from
user-secrets after the first successful initialization. Admin passwords can thereafter
be changed using the emailed reset link.

If an admin account already exists in the database, changing its email requires a
database/admin-account maintenance operation; changing `Admin:Email` will not overwrite
the stored address.

For local development recovery when SMTP is unavailable, run the app in the Development
environment and open `/Admin/LocalRecovery` from the same computer. The page accepts only
loopback requests, resets the username to `admin`, and displays a cryptographically
generated temporary password once. The admin must change it to a new password of
6 to 24 characters before accessing the portal. This recovery page is unavailable outside
Development and must not be exposed through a public reverse proxy.

## Configure password-reset email

Set these values with user-secrets locally or a secret manager in production:

```powershell
dotnet user-secrets set "Email:SmtpHost" "<SMTP host>"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:Username" "<SMTP username>"
dotnet user-secrets set "Email:Password" "<SMTP password>"
dotnet user-secrets set "Email:FromAddress" "<verified sender address>"
dotnet user-secrets set "Email:FromName" "DepEd DTR"
dotnet user-secrets set "Email:UseSsl" "true"
dotnet user-secrets set "App:PublicBaseUrl" "https://<your-public-app-host>"
```

For a local-only test, use the local app URL for `App:PublicBaseUrl`. Reset tokens expire
after 20 minutes, are stored only as hashes, and are invalidated after use. The forgot
password page gives the same response whether or not an account matches the submitted
email.

Form 48 QR codes contain protected verification links, not attendance details. Scanning
the code checks the report against current database records and shows only the employee,
school, and report period. Configure `App:PublicBaseUrl` to the publicly reachable HTTPS
address in production; HTTP is accepted only in development. Keep the ASP.NET Core Data
Protection key ring persistent across restarts and shared by all app instances so issued
QR codes remain verifiable.
For a phone to scan a locally generated code, set `App:PublicBaseUrl` to an address
reachable from that phone on the same network; `localhost` refers to the phone itself.

The app applies database migrations on startup. Production deployments should terminate
TLS at the app or a correctly configured trusted reverse proxy, retain ASP.NET Core
Data Protection keys between restarts, and keep all secret configuration outside the
repository.

## Staff portal invitations

An administrator can invite an active employee from the Employee Management directory.
The employee must have a unique school email address. The invitation link expires after
48 hours and can only be used once; employees choose a password of 6 to 24 characters
when activating their account. Employee Management now generates a one-time invitation
link for the administrator to copy and send to the employee using the saved email address.
SMTP and `App:PublicBaseUrl` are not required for this manual invitation flow. Changing an
employee's email invalidates any pending invite and requires the administrator to generate
a new one. Employees sign in at
`/Employee/Login`, view and print only their own monthly attendance records, and submit
leave requests. Administrators can review pending requests at `/Admin/Leaves` and
prepare monthly Form 48 reports at `/Admin/Reports`.

Administrators can bulk-add employees from Employee Management: **Download CSV template**
(columns `Badgenumber,Name,TITLE`), fill it in, then **Import CSV**. Badgenumber becomes the
Employee ID, Name is split into first/middle/last name (write the middle name as an initial,
e.g. `Juan Drew L. Delo Santos`), and TITLE becomes the Position. Existing Badgenumbers are
skipped, problem rows are reported and not saved, and imported employees start as Active.

Administrators can import biometric attendance text files from **DTR Reports → Import
attendance log**. Column 1 is the employee ID (Badgenumber), column 2 the date and time, and
column 6 is `I` (in) or `O` (out). Before 12:00 is AM and 12:00 onward is PM (a Time Out
from 12:00 to 12:59 counts as the lunch/AM Time Out). A preview lets the administrator pick
which scan to keep when an In or Out is duplicated, or when it differs from a saved time;
nothing is written until the preview is confirmed, and each saved day is recorded in the audit
log.

Administrators can edit daily AM/PM attendance entries from DTR Reports. These changes
are recorded in the admin-only edit history with the editor, timestamp, and note; the
notes are not shown on or printed with Form 48.

Employee password resets use the temporary password `deped123`; employees are required
to change it at their next sign-in. New employee passwords must be 6 to 24 characters.

Employees can submit seminar details from **My seminars**. Administrators validate
requests at **Seminar requests**; only approved seminar dates and titles are reflected in
the employee's monthly Form 48 and its QR verification.

Employees manage their own contact details, profile photo (JPG, PNG or WebP, up to 2 MB,
stored in the database and served only to the signed-in owner) and password from the
**Information** tab. Changing the password requires the current password. Name, position,
department, school and sign-in email remain administrator-managed.
