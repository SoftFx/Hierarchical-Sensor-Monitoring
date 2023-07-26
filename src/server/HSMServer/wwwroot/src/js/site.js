﻿window.copyToClipboard = function(text) {
    const copyToClipboardAsync = str => {
        if (navigator && navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(str);
        }
        return Promise.reject('The Clipboard API is not available.');
    };

    copyToClipboardAsync(text);
    showToast("Copied!");
}

window.showToast = function (message, header = 'Info') {
    document.getElementById('toast_body').innerHTML = message;
    document.getElementById('toast_header').innerHTML = header;
    let currentToast = document.getElementById('liveToast')
    let currentToastInstance = bootstrap.Toast.getOrCreateInstance(currentToast)
    currentToastInstance.show();
}

window.markdownToHTML = function (text) {
    return window.DOMPurify.sanitize(window.marked.marked(text));
}

window.replaceHtmlToMarkdown = function (elementId) {
    let element = $(`#${elementId}`);
    let innerHtml = element.html();

    if (innerHtml !== undefined) {
        element.empty().append(markdownToHTML(innerHtml));
        element.children().last().css('margin-bottom', 0);
    }
}