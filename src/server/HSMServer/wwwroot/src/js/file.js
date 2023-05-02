//Mime types map
const mimeTypesMap = new Map();
mimeTypesMap.set('html', 'text/html');
mimeTypesMap.set('pdf', 'application/pdf');
mimeTypesMap.set('csv', 'application/csv');

window.openFileInBrowser = function(path, fileName, viewFileAction, time = undefined) {
    let fileType = getMimeType(fileName);
    
    $("#spinner").css("display", "block");
    $("#mainContainer").css("display", "none");
    $("#navbar").css("display", "none");

    let url = time === undefined ? `${viewFileAction}?Selected=${path}` : `${viewFileAction}?Selected=${path}&dateTime=${time}`;
    $.ajax({
        type: 'POST',
        url: url,
        cache: false,
        contentType: "application/json",
        success: function (response) {
            if (fileType === undefined) {
                fileType = "text/html";
            }
            
            let win = window.open('/Home/FilePreview','_blank');
            
            if (fileType === 'application/csv'){
                let data = [];
                response.split('\n').forEach( el => {
                    data.push(el.split(','))
                });

                win.onload = (event) => {
                    win.document.getElementById('preview').innerHTML = response;
                    win.openHeihoCSV(data);
                    win.document.getElementById('heiho-view-close').remove();
                };
            }
            else {
                win.onload = (event) => {
                    let iframedoc = win.document.getElementById('preview').contentWindow.document
                    iframedoc.open()
                    iframedoc.write(response)
                    iframedoc.close()
                };
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