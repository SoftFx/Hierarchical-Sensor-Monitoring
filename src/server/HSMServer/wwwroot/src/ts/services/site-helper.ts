import DOMPurify from 'dompurify';
import {getMarkdown} from "../../js/site";

export namespace SiteHelper {
    export function replaceHtmlToMarkdown(elementId: string) {
        let element = $(`#${elementId}`);
        let innerHtml = element.html();

        if (innerHtml !== undefined) {
            element.empty().append(markdownToHTML(innerHtml));
            element.children().last().css('margin-bottom', 0);
        }
    }

    export function markdownToHTML(text: string) {
        return DOMPurify.sanitize(getMarkdown(text));
    }

    export function showToast(message: string, header = 'Info') {
        document.getElementById('toast_body').innerHTML = message;
        document.getElementById('toast_header').innerHTML = header;
        let currentToast = document.getElementById('liveToast')
        // @ts-ignore
        let currentToastInstance = bootstrap.Toast.getOrCreateInstance(currentToast)
        currentToastInstance.show();
    }

    export function ManualCheckDashboardBoundaries() {
        let targetNode = $('#dashboardPanels');
        let height = 0;
        for (const mutation of targetNode.find('.panel')) {
            if (mutation.querySelector('table.orderable-table') !== null) {
                let target = mutation.getBoundingClientRect();
                let parentTarget = targetNode[0].getBoundingClientRect()
                let targetHeight = Number(mutation.getAttribute('data-y')) + target.height;
                if (targetHeight > parentTarget.height)
                    height = targetHeight;
            }
        }

        if (height !== 0) {
            targetNode[0].style.minHeight = height + 30 + 'px';
            targetNode[0].scrollIntoView({behavior: "instant", block: "end", inline: "nearest"});
        }
    }
}