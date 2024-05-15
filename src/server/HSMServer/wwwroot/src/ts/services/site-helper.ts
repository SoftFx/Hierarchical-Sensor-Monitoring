import DOMPurify from 'dompurify';
import {getMarkdown} from "../../js/site";

export namespace SiteHelper {
    export function replaceHtmlToMarkdown (elementId: string) {
        let element = $(`#${elementId}`);
        let innerHtml = element.html();

        if (innerHtml !== undefined) {
            element.empty().append(markdownToHTML(innerHtml));
            element.children().last().css('margin-bottom', 0);
        }
    }

    export function markdownToHTML (text: string) {
        return DOMPurify.sanitize(getMarkdown(text));
    }
}