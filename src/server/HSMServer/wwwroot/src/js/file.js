﻿//Mime types map
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
        success: function (response) {
            if (fileType === undefined || fileType === "application/csv") {
                fileType = "text/html";
            }

            let blob = new Blob([response], { type: fileType });
            let url = window.URL.createObjectURL(blob);
            window.open(url);

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
        success: function (file) {
            if (extension === 'csv') {
                let dataSet = csvStringToArray(file);

                let columns = [];

                dataSet[0].forEach(el => {
                    columns.push({ "title": el })
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
                $(`#preview-content${fileId}`).addClass('w-100 mw-100 text-break').html(file);
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