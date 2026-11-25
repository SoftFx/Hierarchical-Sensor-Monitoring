//Mime types map
const mimeTypesMap = new Map();
mimeTypesMap.set('html', 'text/html');
mimeTypesMap.set('pdf', 'application/pdf');
mimeTypesMap.set('csv', 'application/csv');

window.openFileInBrowser = function (path, fileName, viewFileAction, time = undefined) {
    let fileType = getMimeType(fileName);

    $("#spinner").css("display", "block");
    $("#navbar").css("display", "none");
    $("#mainContainer").css("display", "none");

    let url = time === undefined ? `${viewFileAction}?Selected=${path}` : `${viewFileAction}?Selected=${path}&dateTime=${time}`;
    $.ajax({
        type: 'POST',
        url: url,
        cache: false,
        contentType: "application/json",
        // The response is the file itself, and its content type follows the file's extension. It is taken
        // as text, whatever it claims to be: jQuery must never guess a type and evaluate it.
        dataType: "text",
        success: function (response) {
            if (fileType === undefined || fileType === "application/csv") {
                fileType = "text/html";
            }

            openFileContent(response, fileType);

            $("#spinner").css("display", "none");
            $("#mainContainer").css("display", "block");
            $("#navbar").css("display", "block");
        },
        error: function (_) {
            $("#spinner").css("display", "none");
            $("#mainContainer").css("display", "block");
            $("#navbar").css("display", "block");
        }
    });
}

// The content of a file sensor comes from whoever holds an access key of the product. A PDF goes to the
// browser's viewer. Anything else is shown as a page, but inside a sandboxed frame: the page may run its
// own scripts, in an origin of its own, with no access to this site's cookies, storage or pages.
function openFileContent(content, fileType) {
    if (fileType === 'application/pdf') {
        window.open(window.URL.createObjectURL(new Blob([content], { type: fileType })));
        return;
    }

    const page = window.open('', '_blank');

    if (!page)
        return;

    const frame = page.document.createElement('iframe');

    frame.setAttribute('sandbox', 'allow-scripts allow-forms allow-popups allow-modals');
    frame.setAttribute('style', 'position:fixed;top:0;left:0;width:100%;height:100%;border:0');
    frame.srcdoc = typeof content === 'string' ? content : String(content);

    page.document.title = 'File preview';
    page.document.body.style.margin = '0';
    page.document.body.appendChild(frame);
}

const csvStringToArray = (data) => {
    const re = /(,|\r?\n|\r|^)(?:"([^"]*(?:""[^"]*)*)"|([^,\r\n]*))/gi
    const result = [[]]
    let matches
    while ((matches = re.exec(data))) {
        if (matches[1].length && matches[1] !== ',') result.push([])
        result[result.length - 1].push(
            matches[2] !== undefined ? matches[2].replace(/""/g, '"') : matches[3]
        )
    }
    
    result.pop();
    return result
}

window.previewFile = function(url, id, extension, time = null, fileNumber = null){
    let fileId = '';
    if (time === null)
        url = `${url}?Selected=${id}`;
    else
    {
        url = `${url}?Selected=${fileNumber}&datetime=${time}`
        fileId = `_${id}`;
    }

    $.ajax({
        type: 'POST',
        url: url,
        cache: false,
        contentType: "application/json",
        dataType: "text",
        success: function (file) {
            if (extension === 'csv') {
                let dataSet = csvStringToArray(file);

                let columns = [];

                // DataTables takes titles and cells as HTML: the file's text is shown as text.
                dataSet[0].forEach(el => {
                    columns.push({ "title": escapeHtml(el), "render": $.fn.dataTable.render.text() })
                });

                $(`#preview${fileId}`).removeClass('d-none container');
                $(`#preview-content${fileId}`).html('<table class="display w-100" id="' + `example${fileId}"` + '></table>');

                $(`#example${fileId}`).dataTable({
                    "data": dataSet.splice(1),
                    "columns": columns,
                    "lengthMenu": [[5, 20, 50, 100], [5, 20, 50, 100]],
                    "pageLength": 20
                });
            }
            else if (extension === 'txt') {
                $(`#preview${fileId}`).removeClass('d-none container');
                $(`#preview-content${fileId}`).addClass('w-100 mw-100 text-break').text(file);
            }
        }
    })
}

//files functionality
function getMimeType(fileName) {
    let extension = getExtensionFromName(fileName);
    let fileType = mimeTypesMap.get(extension);
    if (fileType === undefined) {
        fileType = "text/html";
    }
    return fileType;
}

function getExtensionFromName(fileName) {
    let dotIndex = fileName.indexOf('.');
    if (dotIndex === -1) {
        return fileName;
    }
    return fileName.substring(dotIndex + 1, fileName.length);
}