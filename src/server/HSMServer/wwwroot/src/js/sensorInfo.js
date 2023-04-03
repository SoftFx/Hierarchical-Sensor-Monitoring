﻿window.initializeInfoLinks = function() {
    //$('[id^="sensorInfo_link_"]').off("click").on("click", metaInfoLinkClicked);
}

window.editInfoButtonClick = function () {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    $('#interval_' + sensorId).removeAttr("disabled");
    $('#description_' + sensorId).removeAttr("disabled").removeClass("naked-text");
    $('#unit_' + sensorId).removeAttr("disabled").removeClass("naked-text");
    $('#saveInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#revertInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#editButtonMetaInfo').attr('hidden', true);

    $('#editSensorMetaInfo_form').children('div').each(function () {
        $(this).removeClass('d-none');
    });
    $('[id^="span_description_"]').addClass('d-none')
    $('[id^="description_"]').removeClass('d-none')
    
    let collapse = '#sensor_info_collapse';
    if ($(collapse).attr('aria-expanded') === 'false'){
        $(collapse).click()
    }
    
    $('#partialIntervalSelect').removeClass('d-none');
    $('#partialRestoreSelect').removeClass('d-none');

    $('#labelInterval').addClass('d-none');
    $('#labelRestoreInterval').addClass('d-none');

    $(`#expectedUpdateInterval_${sensorId}:input`).each(() => this.removeAttribute('disabled'));
    $(`#restorePolicy_${sensorId}:input`).each(() => this.removeAttribute('disabled'));
}

window.revertInfoClick = function () {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    showMetaInfo(sensorId);
}

window.displaySensorMetaInfo = function (sensorId, viewData) {
    $('#sensor_info_' + sensorId).html(viewData);
    $('#sensor_info_collapse').click();

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
        console.log('showmetainfo')
        displaySensorMetaInfo(id, data);
        //setLinkText(id, "Hide meta info");
    });
}

function hideMetaInfo(sensorId) {
    //$('#sensor_info_' + sensorId).empty();
    //setLinkText(sensorId, "Show meta info");
}

function setLinkText(sensorId, text) {
    //let link = document.getElementById('sensorInfo_link_' + sensorId);
    //link.textContent = text;
}

function disableExpectedUpdateIntervalControl() {
    let sensorId = $('#sensorMetaInfo_encodedId').val();

    $(`#expectedUpdateInterval_${sensorId}:input`).each(() => this.setAttribute('disabled', true));
    $(`#restorePolicy_${sensorId}:input`).each(() => this.setAttribute('disabled', true));
}