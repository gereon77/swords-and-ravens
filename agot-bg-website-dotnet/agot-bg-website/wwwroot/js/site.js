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

// Single shared modal (Pages/Shared/_Layout.cshtml) used by every "gear" button on the Games/
// MyGames/User games-list tables to summarize a game's setup/settings - event-delegated so it
// works for any number of rows (the User profile page alone can have 900+) without per-row
// listeners or per-row <dialog> elements bloating the DOM.
document.addEventListener("click", function (event) {
    var gearButton = event.target.closest(".js-game-settings-btn");
    if (!gearButton) {
        return;
    }

    var modal = document.getElementById("game-settings-modal");
    if (!modal) {
        return;
    }

    modal.querySelector("#game-settings-modal-title").textContent = gearButton.dataset.gameName || "";
    modal.querySelector("#game-settings-modal-setup").textContent = gearButton.dataset.setupName || "";
    modal.querySelector("#game-settings-modal-players").textContent = gearButton.dataset.playerCount || "";

    var settingsList = modal.querySelector("#game-settings-modal-settings");
    var noSettingsMessage = modal.querySelector("#game-settings-modal-no-settings");
    settingsList.innerHTML = "";
    var settings = (gearButton.dataset.settings || "").split("|").filter(function (s) { return s.length > 0; });
    if (settings.length === 0) {
        noSettingsMessage.classList.remove("hidden");
    } else {
        noSettingsMessage.classList.add("hidden");
        settings.forEach(function (label) {
            var li = document.createElement("li");
            li.textContent = label;
            settingsList.appendChild(li);
        });
    }

    modal.showModal();
});

// Persist the open/collapsed state of the <details> "collapse" sections on Games/MyGames/User
// (games/cancelled games/previously participated games), keyed by a per-section
// `data-collapse-key` attribute set in the Razor markup. Only writes a cookie on toggle - the
// *reverse* direction (applying the saved state on load) is done server-side, by
// CollapseStateExtensions.IsCollapseSectionOpen reading this same cookie and rendering the
// correct `open` attribute directly into the initial HTML. An earlier version of this feature
// used localStorage and re-applied the state client-side via JS after the page had already
// painted with the server's default state, which caused a visible layout shift/jank (the section
// would render open, then immediately snap shut once the script ran) - baking the state into the
// server render instead avoids any client-side recalculation entirely.
document.addEventListener(
    "toggle",
    function (event) {
        var details = event.target;
        if (!(details instanceof HTMLDetailsElement)) {
            return;
        }
        var key = details.dataset.collapseKey;
        if (!key) {
            return;
        }
        var oneYearInSeconds = 60 * 60 * 24 * 365;
        document.cookie =
            "snr_collapse_" + key + "=" + (details.open ? "open" : "closed") +
            "; path=/; max-age=" + oneYearInSeconds + "; samesite=lax";
    },
    true // the native "toggle" event does not bubble, so this must be a capturing listener
);
