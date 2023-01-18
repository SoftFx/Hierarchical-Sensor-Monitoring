window.copyToClipboard = function(text) {
    const copyToClipboardAsync = str => {
        if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(str);
        }
        return Promise.reject('The Clipboard API is not available.');
    };

    copyToClipboardAsync(text);
    showToast("Copied!");
}

window.showToast = function (message){
    document.getElementById('toast_body').innerHTML = message;
    let currentToast = document.getElementById('liveToast')
    let currentToastInstance = bootstrap.Toast.getOrCreateInstance(currentToast)
    currentToastInstance.show();
}