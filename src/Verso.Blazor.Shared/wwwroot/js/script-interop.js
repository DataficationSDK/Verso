window.versoScripts = {
    /**
     * Finds all containers matching the selector and activates their script elements.
     * External scripts are loaded in order (waiting for each to finish) before
     * inline scripts execute, solving the race condition where innerHTML-injected
     * inline JS runs before its dependent library has loaded.
     */
    activateAll: async function (selector) {
        var containers = document.querySelectorAll(selector);
        for (var i = 0; i < containers.length; i++) {
            await this._activate(containers[i]);
        }
    },

    _activate: async function (container) {
        var scripts = container.querySelectorAll('script');
        if (scripts.length === 0) return;

        var external = [];
        var inline = [];

        for (var i = 0; i < scripts.length; i++) {
            if (scripts[i].src) {
                external.push(scripts[i]);
            } else {
                inline.push(scripts[i]);
            }
        }

        // Load external scripts sequentially, skipping any already loaded
        for (var j = 0; j < external.length; j++) {
            var src = external[j].src;
            if (document.querySelector('script[data-verso-loaded="' + src + '"]')) continue;

            await new Promise(function (resolve) {
                var el = document.createElement('script');
                el.src = src;
                el.setAttribute('data-verso-loaded', src);
                el.onload = resolve;
                el.onerror = function () {
                    console.error('Failed to load script:', src);
                    resolve();
                };
                document.head.appendChild(el);
            });
        }

        // Execute inline scripts in order by replacing them with fresh elements
        for (var k = 0; k < inline.length; k++) {
            try {
                var el = document.createElement('script');
                el.textContent = inline[k].textContent;
                inline[k].parentNode.replaceChild(el, inline[k]);
            } catch (e) {
                console.error('Inline script error:', e);
            }
        }
    }
};
