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


//function getFileName(product, path, fileName) {
//    let newDate = new Date();
//    let date = newDate.toLocaleDateString("ru-RU");
//    let time = newDate.toLocaleTimeString("ru-Ru").replace(':', '.');
//    let dotIndex = fileName.indexOf('.');
//    //has dot and does not start from dot
//    if (dotIndex > 0) {
//       return fileName;

//    }

//    if (dotIndex === 0) {
//        return product + "_" + path + "_" + date + "_" + time + fileName;
//    }
//    return product + "_" + path + "_" + date + "_" + time + "." + fileName;
//}

function viewFile(product, path, fileName, viewFileAction) {
    let fileType = getMimeType(fileName);
    //console.log(fileType);
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
    $.ajax({
        type: 'POST',
        data: JSON.stringify(fileData(product, path)),
        url: viewFileAction,
        cache: false,
        contentType: "application/json",
        success: function (response) {
            if (fileType === undefined) {
                fileType = "text/html";
            }
            let blob = new Blob([response], { type: fileType });
            let url = window.URL.createObjectURL(blob);
            window.open(url);
        }
    });
}

function fileData(product, path) {
    return { "Product": product, "Path": path };
}