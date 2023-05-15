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