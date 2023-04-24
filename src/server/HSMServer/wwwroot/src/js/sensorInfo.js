window.editInfoButtonClick = function () {
    let sensorId = $('#metaInfo_encodedId').val();

    $('#interval_' + sensorId).removeAttr("disabled");
    $('#description_' + sensorId).removeAttr("disabled").removeClass("naked-text");
    $('#saveInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#revertInfo_' + sensorId).removeAttr("disabled").removeAttr("hidden");
    $('#editButtonMetaInfo').addClass('d-none');

    $('#editMetaInfo_form').children('div').each(function () {
        $(this).removeClass('d-none');
    });
    $('[id^="markdown_span_description_"]').addClass('d-none')
    $('[id^="description_"]').removeClass('d-none')
    $('#meta_info_collapse').addClass('d-none')
   
    $('#metainfo_separator').removeClass('d-none');
    
    $('#partialIntervalSelect').removeClass('d-none');
    $('#partialRestoreSelect').removeClass('d-none');

    $('#labelInterval').addClass('d-none');
    $('#labelRestoreInterval').addClass('d-none');
}

window.revertInfoClick = function () {
    let sensorId = $('#metaInfo_encodedId').val();

    showMetaInfo(sensorId);
}

window.displaySensorMetaInfo = function (sensorId, viewData) {
    $('#meta_info_' + sensorId).html(viewData);
   
    let metaInfo = $('#metaInfoCollapse');

    metaInfo.addClass('no-transition');
    $('#meta_info_collapse').click();
    metaInfo.removeClass('no-transition');
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
    });
}