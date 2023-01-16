window.copyToClipboard = function(text) {
    const copyToClipboardAsync = str => {
        if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(str);
        }
        return Promise.reject('The Clipboard API is not available.');
    };

    copyToClipboardAsync(text);
    showToast("Copied to clipboard");
}

window.showToast = function (message){
    document.getElementById('toast_body').innerHTML = message;
    let myToastEl = document.getElementById('liveToast')
    let instance = bootstrap.Toast.getOrCreateInstance(myToastEl)
    instance.show();
}