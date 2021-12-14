//Mime types map
const mimeTypesMap = new Map();
mimeTypesMap.set('html', 'text/html');
mimeTypesMap.set('pdf', 'application/pdf');

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

function openFileInBrowser(path, fileName, viewFileAction) {
    let fileType = getMimeType(fileName);
    //var xhr = new XMLHttpRequest();
    //xhr.open('POST', viewFileAction, true);
    //xhr.responseType = 'blob';
    //xhr.onload = function () {
    //    let blob = new Blob([this.response], { type: fileType });
    //    console.log(blob);
    //    let url = window.URL.createObjectURL(blob);
    //    window.open(url);
    //}
    //xhr.send(JSON.stringify(fileData(product, path)));

    $("#spinner").css("display", "block");
    $("#mainContainer").css("display", "none");
    $("#navbar").css("display", "none");

    $.ajax({
        type: 'POST',
        url: viewFileAction + "?Selected=" + path,
        cache: false,
        contentType: "application/json",
        success: function (response) {
            if (fileType === undefined) {
                fileType = "text/html";
            }

            let blob = new Blob([response], { type: fileType });
            let url = window.URL.createObjectURL(blob);
            window.open(url);

            $("#spinner").css("display", "none");
            $("#mainContainer").css("display", "block");
            $("#navbar").css("display", "block");
        },
        error: function(error){
            console.log("Failed to load file!");

            $("#spinner").css("display", "none");
            $("#mainContainer").css("display", "block");
            $("#navbar").css("display", "block");
        }
    });
}