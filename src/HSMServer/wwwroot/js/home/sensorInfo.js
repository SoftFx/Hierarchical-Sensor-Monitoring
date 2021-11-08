function initializeInfoLinks() {
    $('[id^="sensorInfo_link_"]').off("click").on("click", metaInfoLinkClicked);
}

function metaInfoLinkClicked() {
    let path = this.id.substring("sensorInfo_link_".length);
    if ($('#sensor_info_' + path).is(':empty')) {
        showMetaInfo(path);
    } else {
        hideMetaInfo(path);
    }
}

function showMetaInfo(path) {
    $.ajax({
        type: 'GET',
        url: getSensorInfoAction + "?Path=" + path,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        $('#sensor_info_' + path).empty().append(JSON.parse(data).value);
        setLinkText(path, "Hide meta info");
        initializeEditInfoButtons(path);
    });
}

function hideMetaInfo(path) {
    $('#sensor_info_' + path).empty();
    setLinkText(path, "Show meta info");
}

function setLinkText(path, text) {
    let link = document.getElementById('sensorInfo_link_' + path);
    link.textContent = text;
}

function initializeEditInfoButtons(path) {
    $('#editInfo_' + path).on("click", editInfoButtonClick);
    $('#revertInfo_' + path).on("click", revertInfoClick);
    $('#saveInfo_' + path).on("click", saveInfoClick);
}

function editInfoButtonClick() {
    let path = this.id.substring("editInfo_".length);
    $('#interval_' + path).removeAttr("disabled");
    $('#description_' + path).removeAttr("disabled");
    $('#saveInfo_' + path).removeAttr("disabled");
    $('#revertInfo_' + path).removeAttr("disabled");
}

function revertInfoClick() {
    let path = this.id.substring("revertInfo_".length);
    reloadInfo(path);
}

function saveInfoClick() {
    let path = this.id.substring('saveInfo_'.length);
    let description = getDescription(path);
    let interval = getInterval(path);
    let body = Info(description, interval, path);
    saveSensorInfo(body);
}

function saveSensorInfo(body) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: updateSensorInfoAction,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function () {
        reloadInfo(body.EncodedPath);
    });
}

function Info(description, updatePeriod, encodedPath) {
    return { "Description": description, "ExpectedUpdateInterval": updatePeriod, "EncodedPath": encodedPath };
}

function getDescription(path) {
    return $('#description_' + path).val();
}

function getInterval(path) {
    return $('#interval_' + path).val();
}

function reloadInfo(path) {
    let link = document.getElementById('sensorInfo_link_' + path);
    link.click();
    link.click();
}