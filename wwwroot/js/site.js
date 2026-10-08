// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Adds a Show/Hide toggle to every password field.
document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("input[type='password']").forEach(function (input) {
        var group = document.createElement("div");
        group.className = "input-group";
        input.parentNode.insertBefore(group, input);
        group.appendChild(input);

        var toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "btn btn-outline-secondary";
        toggle.textContent = "Show";
        toggle.setAttribute("aria-label", "Show password");
        toggle.setAttribute("aria-pressed", "false");
        group.appendChild(toggle);

        toggle.addEventListener("click", function () {
            var reveal = input.type === "password";
            input.type = reveal ? "text" : "password";
            toggle.textContent = reveal ? "Hide" : "Show";
            toggle.setAttribute("aria-label", reveal ? "Hide password" : "Show password");
            toggle.setAttribute("aria-pressed", String(reveal));
            input.focus();
        });
    });
});
