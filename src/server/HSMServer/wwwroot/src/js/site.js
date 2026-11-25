import {MutationObserverService} from "../ts/services/mutation-observer-service";

window.createFormObserver = function (id){
    const obs = new MutationObserverService();
    obs.addFormToObserve(id);
    return obs; 
}

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

const htmlEscapes = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

// Text that goes into an HTML string (a name, a path, a server message) passes through this first:
// it then shows as the text it is, whatever characters it carries.
window.escapeHtml = function (text) {
    return (text === null || text === undefined ? '' : String(text)).replace(/[&<>"']/g, c => htmlEscapes[c]);
}

// For the helpers that take ready HTML (toast, confirmation dialog). The markup the UI builds itself
// stays (line breaks, emphasis, coloured spans, links); scripts, event handlers and javascript: links
// do not. Callers still escape the text they put into that HTML: this is the second line, not the first.
window.sanitizeHtml = function (html) {
    return window.DOMPurify.sanitize(html === null || html === undefined ? '' : String(html), { ADD_ATTR: ['target'] });
}

window.showToast = function (message, header = 'Info') {
    document.getElementById('toast_body').innerHTML = sanitizeHtml(message);
    document.getElementById('toast_header').innerHTML = sanitizeHtml(header);
    let currentToast = document.getElementById('liveToast')
    let currentToastInstance = bootstrap.Toast.getOrCreateInstance(currentToast)
    currentToastInstance.show();
}

window.markdownToHTML = function (text) {
    return window.DOMPurify.sanitize(window.marked.marked(text));
}

export function getMarkdown(text) {
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

window.getCurrentUpdateTreeInterval =  function () {
    return document.getElementById('treeInterval').value * 1000;
}