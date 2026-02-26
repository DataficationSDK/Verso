window.versoRecovery = {
    _keyPath: 'verso-recovery-path',
    _keyContent: 'verso-recovery-content',
    _keyTimestamp: 'verso-recovery-ts',

    saveState: function (filePath, serializedContent) {
        try {
            sessionStorage.setItem(this._keyPath, filePath || '');
            sessionStorage.setItem(this._keyContent, serializedContent || '');
            sessionStorage.setItem(this._keyTimestamp, Date.now().toString());
        } catch (e) {
            // sessionStorage full or unavailable — silently ignore
        }
    },

    getState: function () {
        try {
            var path = sessionStorage.getItem(this._keyPath);
            var content = sessionStorage.getItem(this._keyContent);
            var ts = sessionStorage.getItem(this._keyTimestamp);
            if (!content && !path) return null;
            return { filePath: path || '', content: content || '', timestamp: ts ? parseInt(ts, 10) : 0 };
        } catch (e) {
            return null;
        }
    },

    clearState: function () {
        try {
            sessionStorage.removeItem(this._keyPath);
            sessionStorage.removeItem(this._keyContent);
            sessionStorage.removeItem(this._keyTimestamp);
        } catch (e) {
            // ignore
        }
    }
};
