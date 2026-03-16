
window.showScheduleModal = function () {
    $('#alertScheduleModal').modal({
        backdrop: 'static'
    });
    $('#alertScheduleModal').modal('show');
};

window.hideScheduleModal = function () {
    $('#alertScheduleModal').modal('hide');
};

window.setScheduleModalTitle = function (title) {
    $('#alertScheduleModalTitle').empty().append(title);
};

window.setScheduleModalBody = function (html) {
    $('#alertScheduleModalBody').html(html);
};

window.showLargeScheduleModal = function () {
    var dialog = document.getElementById("alertScheduleModalDialog");
    if (dialog) {
        dialog.classList.remove("w-50", "w-75", "w-100");
        dialog.classList.add("w-50");
    }
};

window.showMiddleScheduleModal = function () {
    var dialog = document.getElementById("alertScheduleModalDialog");
    if (dialog) {
        dialog.classList.remove("w-75", "w-100");
        dialog.classList.add("w-50");
    }
};

function loadScheduleForm(url, title) {
    $.ajax({
        type: 'GET',
        url: url,
        cache: false,
        success: function (viewData) {
            setScheduleModalBody(viewData);
            setScheduleModalTitle(title);

            // Инициализируем CodeMirror после обновления DOM
            if (window.initAlertScheduleEditor) {
                // Небольшая задержка для гарантии, что DOM обновлён
                setTimeout(() => {
                    window.initAlertScheduleEditor('Schedule', 'alert-schedule-editor-container');
                }, 0);
            }

            showLargeScheduleModal();
            showScheduleModal();
        }
    });
}


$(document).ready(function () {

    $('#addScheduleBtn').on('click', function () {
        loadScheduleForm(scheduleUrls.newPartial, 'New alert schedule');
    });

    $(document).on('click', '.schedule-edit', function () {
        var id = $(this).data('id');
        var name = $(this).data('name');
        var url = scheduleUrls.editPartial + '?id=' + id;
        loadScheduleForm(url, 'Edit alert schedule: ' + name);
    });

    $(document).on('submit', '#alertScheduleForm', function (event) {
        event.preventDefault();
        event.stopImmediatePropagation();

        var form = $(this);
        var formData = new FormData(this);

        $.ajax({
            url: form.attr('action'),
            type: 'POST',
            data: formData,
            processData: false,
            contentType: false,
            cache: false,
            success: function (result) {
                if ($(result).is('table') || $(result).find('table').length > 0) {
                    $('#alertSchedulesTableContainer').html(result);
                    hideScheduleModal();
                } else {
                    setScheduleModalBody(result);
                    setTimeout(() => {
                        window.initAlertScheduleEditor('Schedule', 'alert-schedule-editor-container');
                    }, 0);
                }
            }
        });
    });


    $('#alertScheduleModal').on('hidden.bs.modal', function () {
        var dialog = document.getElementById("alertScheduleModalDialog");
        if (dialog) {
            dialog.classList.remove("w-50", "w-75", "w-100");
        }
    });
});