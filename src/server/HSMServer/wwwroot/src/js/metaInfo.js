window.editInfoButtonClick = function () {
    $('#saveInfo').removeAttr("hidden");
    $('#revertInfo').removeAttr("hidden");
    $('#editButtonMetaInfo').addClass('d-none');

    $('#editMetaInfo_form').children('div').each(function () {
        $(this).removeClass('d-none');
    });

    $('#description').removeClass('d-none')
    $('#metainfo_separator').removeClass('d-none');
    $('#addDataAlert').removeClass('d-none');
    $('#commentHelp').removeClass('d-none');
    $('[id^="dataAlertsList_"]').removeClass('d-none');

    $('#markdown_span_description').addClass('d-none')
    $('#meta_info_collapse').addClass('d-none')

    $('#partialIntervalSelect').removeClass('d-none');
    $('#partialRestoreSelect').removeClass('d-none');
    $('#partialSavedHistorySelect').removeClass('d-none');
    $('#partialSelfDestroySelect').removeClass('d-none');
    $('[id^="dataAlertInputGroupd_"]').removeClass('d-none');

    $('#labelInterval').addClass('d-none');
    $('#labelRestoreInterval').addClass('d-none');
    $('#labelSavedHistory').addClass('d-none');
    $('#labelSelfDestroy').addClass('d-none');
    $('[id^="dataAlertLabel_"]').addClass('d-none');
}

window.revertInfoButtonClick = function (action) {
    let id = $('#metaInfo_encodedId').val();

    $.ajax({
        type: 'GET',
        url: `${action}?Id=${id}`,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        displayMetaInfo(id, data);
    });
}

window.displayMetaInfo = function (id, viewData) {
    $(`#meta_info_${id}`).html(viewData);
   
    let metaInfo = $('#metaInfoCollapse');

    metaInfo.addClass('no-transition');
    $('#meta_info_collapse').click();
    metaInfo.removeClass('no-transition');

    $('#metainfo_separator').addClass('d-none');
}