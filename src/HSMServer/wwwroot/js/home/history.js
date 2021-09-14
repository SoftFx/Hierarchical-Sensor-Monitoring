Date.prototype.AddDays = function(days) {
    let date = new Date(this.valueOf());
    date.setDate(date.getDate() + days);
    return date;
}

Date.prototype.AddHours = function(hours) {
    let newDate = new Date(this.valueOf());
    newDate.setHours(newDate.getHours() + hours);
    return newDate;
}

function Data(to, from, type, path) {
    return { "To": to, "From": from, "Type": type, "Path": path };
}

//Add event listeners to buttons
function initializeDataHistoryRequests() {
    $('[id^="collapse"]').on('show.bs.collapse', function(e) {
        id = this.id.substring("collapse_".length);
        selected = id;

        totalCount = getCountForId(id);
        type = document.getElementById("sensor_type_" + id).value;
        $('#radio_hour_' + id).attr('checked', 'checked').trigger('click');
    });

    InitializePeriodRequests();
    //$(".accordion-button").on("click", function () {
    //    id = this.id;
    //    selected = id;

    //    totalCount = getCountForId(id);
    //    type = document.getElementById("sensor_type_" + id).value;
    //    $('#radio_hour_' + id).attr('checked', 'checked').trigger('click');

    //    //if (type !== "3" && type !== "6" && type !== "7") {
    //    //    initializeGraph(id, rawHistoryHourAction, type);
    //    //}
    //    //initializeHistory(id, historyHourAction, type);
    //});

    //$('[id^="reload_"]').on("click",
    //    function () {
    //        id = this.id.substring("reload_".length, this.id.length);
    //        totalCount = getCountForId(id);
    //        type = document.getElementById("sensor_type_" + id).value;

    //        if (type !== "3" && type !== "6" && type !== "7") {
    //            initializeGraph(id, type, totalCount, rawHistoryAction);
    //        }
    //        initializeHistory(id, totalCount, historyAction);
    //    });

    $('[id^="button_view"]').on("click",
        function () {
            id = this.id.substring("button_view_".length, this.id.length);
            fileType = document.getElementById('fileType_' + id).value;

            viewFile(id, fileType, viewFileAction);
        }
    );

    $('[id^="button_download"]').on("click",
        function () {
            id = this.id.substring("button_download_".length, this.id.length);

            window.location.href = getFileAction + "?Selected=" + id;
        }
    );
}

function InitializePeriodRequests() {

    $('[id^="radio_hour_"]').off("click").on("click", requestHistoryHour);

    $('[id^="radio_day_"]').off("click").on("click", requestHistoryDay);

    $('[id^="radio_three_days_"]').off("click").on("click", requestHistoryThreeDays);

    $('[id^="radio_week_"]').off("click").on("click", requestHistoryWeek);

    $('[id^="radio_month_"]').off("click").on("click", requestHistoryMonth);

    $('[id^="radio_all_"]').off("click").on("click", requestHistoryAll);

    $('[id^="button_export_csv_"]').off("click").on("click", exportCsv);
}

function requestHistoryHour() {
    let path = this.id.substring("radio_hour_".length);
    let type = getTypeForSensor(path);
    const to = new Date();
    const from = to.AddHours(-1);
    initializeHistories(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
}

function requestHistoryDay() {
    let path = this.id.substring("radio_day_".length);
    let type = getTypeForSensor(path);
    const to = new Date();
    const from = to.AddDays(-1);
    initializeHistories(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
}

function requestHistoryThreeDays() {
    let path = this.id.substring("radio_three_days_".length);
    let type = getTypeForSensor(path);
    const to = new Date();
    const from = to.AddDays(-3);
    initializeHistories(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
}

function requestHistoryWeek() {
    let path = this.id.substring("radio_week_".length);
    let type = getTypeForSensor(path);
    const to = new Date();
    const from = to.AddDays(-7);
    initializeHistories(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
}

function requestHistoryMonth() {
    let path = this.id.substring("radio_month_".length);
    let type = getTypeForSensor(path);
    const to = new Date();
    const from = to.AddDays(-30);
    initializeHistories(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
}

function requestHistoryAll() {
    let path = this.id.substring("radio_all_".length);
    let type = getTypeForSensor(path);
    initializeHistories(path, historyAllAction, rawHistoryAllAction, type, {});
}


function initializeHistories(path, action, rawAction, type, reqData) {
    initializeHistory(path, action, type, reqData);
    if (type !== "3" && type !== "6" && type !== "7") {
        initializeGraph(path, rawAction, type, reqData);
    }
}

function exportCsv() {
    let path = this.id.substring("button_export_csv_".length);
    let type = getTypeForSensor(path);
    if (isAllHistorySelected(path)) {
        window.location.href = exportHistoryAllAction + "?Path=" + path + "&Type=" + type;
        return;
    }

    const {from, to} = getFromAndTo(path);
    window.location.href = exportHistoryAction + "?Path=" + path + "&Type=" + type + "&From=" + from.toISOString() + "&To=" + to.toISOString();
}

function initializeHistory(path, historyAction, type, body) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: historyAction + "?Path=" + path + "&Type=" + type,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function (data) {
        $(`#values_${path}`).empty();
        let values = JSON.parse(data).value;

        if (values === "") {
            $('#history_' + path).hide();
            $('#no_data_' + path).show();
            return;
        }

        $('#history_' + path).show();
        $('#no_data_' + path).hide();
        $(`#values_${path}`).append(values);
    });
}

function isAllHistorySelected(path) {
    return $('#radio_all_' + path).is(":checked");
}

function getFromAndTo(path) {
    let from = null;
    let to = null;
    if ($('#radio_hour_' + path).is(":checked")) {
        to = new Date();
        from = to.AddHours(-1);
    }

    if ($('#radio_day_' + path).is(":checked")) {
        to = new Date();
        from = to.AddDays(-1);
    }

    if ($('#radio_three_days_' + path).is(":checked")) {
        to = new Date();
        from = to.AddDays(-3);
    }

    if ($('#radio_week_' + path).is(":checked")) {
        to = new Date();
        from = to.AddDays(-7);
    }

    if ($('#radio_month_' + path).is(":checked")) {
        to = new Date();
        from = to.AddDays(-30);
    }

    return { from, to };
}
//function getExportAction(path) {
//    if ($('#radio_hour_' + path).is(":checked")) {
//        return exportHistoryHourAction;
//    }
//    if ($('#radio_day_' + path).is(":checked")) {
//        return exportHistoryDayAction;
//    }
//    if ($('#radio_three_days_' + path).is(":checked")) {
//        return exportHistoryThreeDaysAction;
//    }
//    if ($('#radio_week_' + path).is(":checked")) {
//        return exportHistoryWeekAction;
//    }
//    if ($('#radio_month_' + path).is(":checked")) {
//        return exportHistoryMonthAction;
//    }
//    return exportHistoryAllAction;
//}

function getTypeForSensor(name) {
    let element = document.getElementById("sensor_type_" + name);
    return element.value;
}

function getCountForId(id) {
    let inputCount = $('#inputCount_' + id).val();
    if (inputCount === undefined) {

        inputCount = 10;
    }

    return inputCount;
}

//function initializeHistory(path, totalCount, historyAction) {
//    if (totalCount == undefined)
//        totalCount = 10;

//    $.ajax({
//        type: 'POST',
//        data: JSON.stringify(data(path, totalCount)),
//        url: historyAction,
//        dataType: 'html',
//        contentType: 'application/json',
//        cache: false,
//        async: true
//    }).done(function (data) {
//        data = data.replace('{"value":"', ''); //fix sometime
//        data = data.replace('"}', '');

//        $(`#values_${path}`).empty();
//        $(`#values_${path}`).append(data);
//    });
//}

    function initializeGraph(path, rawHistoryAction, type, body) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: rawHistoryAction + "?Path=" + path + "&Type=" + type,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function (data) {
        let graphDivId = "graph_" + path;
        displayGraph(data, type, graphDivId, path);
    });
}