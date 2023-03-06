window.initializeInfoLinks = function() {
    $('[id^="sensorInfo_link_"]').off("click").on("click", metaInfoLinkClicked);
}

window.editInfoButtonClick = function () {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    $('#interval_' + sensorId).removeAttr("disabled");
    $('#description_' + sensorId).removeAttr("disabled").removeClass("naked-text");
    $('#unit_' + sensorId).removeAttr("disabled").removeClass("naked-text");
    $('#saveInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#revertInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#editButtonMetaInfo').attr('hidden', true);
    
    $('#partialIntervalSelect').removeClass('d-none');
    $('#labelInterval').addClass('d-none');

    $('#expectedUpdateInterval_' + sensorId + ' :input').each(function () {
        this.removeAttribute('disabled');
    });
}

window.revertInfoClick = function () {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    showMetaInfo(sensorId);
}

window.displaySensorMetaInfo = function (sensorId, viewData) {
    $('#sensor_info_' + sensorId).html(viewData);

    disableExpectedUpdateIntervalControl();
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
        displaySensorMetaInfo(id, data);
        setLinkText(id, "Hide meta info");
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

function disableExpectedUpdateIntervalControl() {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    $('#expectedUpdateInterval_' + sensorId + ' :input').each(function () {
        this.setAttribute('disabled', true);
    });
}