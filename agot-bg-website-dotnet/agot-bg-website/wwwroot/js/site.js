// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// Bootstrap JS was removed as part of the DaisyUI theme migration. The Identity-scaffolded pages
// still mark dismissible status-message alerts with the old `data-bs-dismiss="alert"` attribute;
// this replaces that behaviour by hiding the alert on click instead of relying on Bootstrap's JS.
document.addEventListener("click", function (event) {
    var dismissTarget = event.target.closest('[data-bs-dismiss="alert"]');
    if (dismissTarget) {
        var alertEl = dismissTarget.closest(".alert");
        if (alertEl) {
            alertEl.classList.add("hidden");
        }
    }
});
