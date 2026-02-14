window.versoFileDownload = {
    triggerDownload: function (fileName, contentType, base64Data) {
        var byteCharacters = atob(base64Data);
        var byteNumbers = new Uint8Array(byteCharacters.length);
        for (var i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        var blob = new Blob([byteNumbers], { type: contentType });
        var url = URL.createObjectURL(blob);
        var anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
    }
};
