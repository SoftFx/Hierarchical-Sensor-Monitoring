//Mime types map
const mimeTypesMap = new Map();
mimeTypesMap.set('html', 'text/html');
mimeTypesMap.set('pdf', 'application/pdf');
mimeTypesMap.set('csv', 'application/csv');

window.openFileInBrowser = function(path, fileName, viewFileAction) {
    let fileType = getMimeType(fileName);
    
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
            
            if (fileType === 'application/csv'){
                let data = [];
                response.split('\n\r').forEach( el => {
                    data.push(el.split(','))
                });
                
                Heiho(data, {
                    header: null,
                    max: 5000
                });
            }
            else {
                let blob = new Blob([response], { type: fileType });
                let url = window.URL.createObjectURL(blob);
                window.open(url);
            }
            
            $("#spinner").css("display", "none");
            $("#mainContainer").css("display", "block");
            $("#navbar").css("display", "block");
        },
        error: function(error){
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