﻿import {currentPanel} from "./dashboard";

window.hiddenColumns = {
    id: undefined,
    
    showText: "Show all columns",
    hideText: "Hide extra columns",
    self: undefined,
    isVisible: false,
    
    tablecellsIds: [], 
    tablecells: [],
    
    hideFromTable(){
        this.tablecells.forEach((x) => {
            x.addClass('d-none')
        })
        this.self[0].innerText = this.showText;
        this.isVisible = false;
    },

    showInTable(){
        this.tablecells.forEach((x) => {
            x.removeClass('d-none')
        })
        this.self[0].innerText = this.hideText;
        this.isVisible = true;
    },
    
    init(id){
        this.self = $('#allColumnsButton');

        this.self.off('click').on('click', function (e) {
            if (hiddenColumns.isVisible)
                hiddenColumns.hideFromTable();
            else
                hiddenColumns.showInTable();
        });
        
        this.self.removeClass('d-none');
        this.tablecells = [];

        this.tablecellsIds.forEach(x => {
            this.tablecells.push($(x))
        })

        if (this.id === id && this.isVisible)
            this.showInTable();
            
        this.id = id;
    },
    
    clear(){
        this.tablecells = [];
        this.tablecellsIds = [];
        this.isVisible = false;
    }
}

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

window.replaceHtmlToMarkdownJquery = function (jqueryElement) {
    let innerHtml = jqueryElement.html();

    if (innerHtml !== undefined) {
        jqueryElement.empty().append(markdownToHTML(innerHtml));
        jqueryElement.children().last().css('margin-bottom', 0);
    }
}

window.redirectToHome = function (id){
    window.localStorage.isDashboardRedirect = true;
    window.location.href = `/Home/${id}`;
}

window.tryValidate = function (element, propertyElement, propertyName) {
    var input = element.find($(`${propertyElement}[name='${propertyName}']`));

    return input.length === 0 || input[0].reportValidity();
}