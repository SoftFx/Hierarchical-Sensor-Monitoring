function initializeInfoLinks() {
    $('[id^="sensorInfo_link_"]').off("click").on("click", metaInfoLinkClicked);
}

function metaInfoLinkClicked() {
    let sensorId = this.id.substring("sensorInfo_link_".length);
    if ($('#sensor_info_' + sensorId).is(':empty')) {
        showMetaInfo(sensorId);
    } else {
        hideMetaInfo(sensorId);
    }
}

function showMetaInfo(id) {
    $.ajax({
        type: 'GET',
        url: getSensorInfoAction + "?Id=" + id,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        $('#sensor_info_' + id).empty().html(data);
        setLinkText(id, "Hide meta info");
        initializeEditInfoButtons(id);
    });
}

function hideMetaInfo(sensorId) {
    $('#sensor_info_' + sensorId).empty();
    setLinkText(sensorId, "Show meta info");
}

function setLinkText(sensorId, text) {
    let link = document.getElementById('sensorInfo_link_' + sensorId);
    link.textContent = text;
}

function initializeEditInfoButtons(sensorId) {
    $('#editInfo_' + sensorId).on("click", editInfoButtonClick);
    $('#revertInfo_' + sensorId).on("click", revertInfoClick);
    $('#saveInfo_' + sensorId).on("click", saveInfoClick);
}

function editInfoButtonClick() {
    let sensorId = this.id.substring("editInfo_".length);

    $('#interval_' + sensorId).removeAttr("disabled");
    $('#description_' + sensorId).removeAttr("disabled");
    $('#unit_' + sensorId).removeAttr("disabled");
    $('#saveInfo_' + sensorId).removeAttr("disabled");
    $('#revertInfo_' + sensorId).removeAttr("disabled");
}

function revertInfoClick() {
    let sensorId = this.id.substring("revertInfo_".length);
    reloadInfo(sensorId);
}

function saveInfoClick() {
    let sensorId = this.id.substring('saveInfo_'.length);
    let description = getDescription(sensorId);
    let interval = getInterval(sensorId);
    let unit = getUnit(sensorId);
    let body = Info(description, interval, sensorId, unit);
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
        reloadInfo(body.EncodedId);
    });
}

function Info(description, updatePeriod, encodedId, unit) {
    return { "Description": description, "ExpectedUpdateInterval": updatePeriod, "EncodedId": encodedId , "Unit": unit };
}

function getDescription(sensorId) {
    return $('#description_' + sensorId).val();
}

function getInterval(sensorId) {
    return $('#interval_' + sensorId).val();
}

function getUnit(sensorId) {
    return $('#unit_' + sensorId).val();
}

function reloadInfo(sensorId) {
    let link = document.getElementById('sensorInfo_link_' + sensorId);
    link.click();
    link.click();
}