window.versoUserPrefs = {
    _keyTheme: 'verso-user-theme',
    _keyDisabledExts: 'verso-user-disabled-extensions',
    _keyThemeCss: 'verso-user-theme-css',

    _isVsCode: function () {
        return window.vscodeBridge && window.vscodeBridge.isVsCodeWebview();
    },

    getTheme: function () {
        try {
            return localStorage.getItem(this._keyTheme);
        } catch (e) {
            return null;
        }
    },

    setTheme: function (themeId) {
        try {
            localStorage.setItem(this._keyTheme, themeId);
        } catch (e) {
            // localStorage full or unavailable
        }
    },

    getDisabledExtensions: function () {
        if (this._isVsCode()) {
            // Route through the VS Code extension host bridge to globalState
            // (webview localStorage doesn't persist across webview instances).
            return window.vscodeBridge.sendRequest("userPrefs/getDisabledExtensions", null)
                .then(function (resultJson) {
                    if (!resultJson) return null;
                    var parsed = JSON.parse(resultJson);
                    return parsed.ids || null;
                })
                .catch(function () { return null; });
        }
        try {
            var json = localStorage.getItem(this._keyDisabledExts);
            if (!json) return null;
            return JSON.parse(json);
        } catch (e) {
            return null;
        }
    },

    setDisabledExtensions: function (ids) {
        if (this._isVsCode()) {
            window.vscodeBridge.sendRequest("userPrefs/setDisabledExtensions", JSON.stringify({ ids: ids }))
                .catch(function () {});
            return;
        }
        try {
            localStorage.setItem(this._keyDisabledExts, JSON.stringify(ids));
        } catch (e) {
            // localStorage full or unavailable
        }
    },

    saveThemeSnapshot: function () {
        try {
            var el = document.getElementById('verso-theme');
            if (el) {
                localStorage.setItem(this._keyThemeCss, el.textContent);
            }
        } catch (e) {
            // ignore
        }
    }
};
