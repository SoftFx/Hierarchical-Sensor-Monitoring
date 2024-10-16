﻿import {GetSensortInfo} from "./metaInfo";

window.getFromAndTo = function (encodedId) {
    let from = $(`#from_${encodedId}`).val();
    let to = $(`#to_${encodedId}`).val();

    if (to == "") {
        to = new Date().AddDays(1);
        $(`#to_${encodedId}`).val(datetimeLocal(to));
    }

    if (from == "") {
        from = to.AddDays(-1);
        $(`#from_${encodedId}`).val(datetimeLocal(from));
    }

    return {from, to};
}

window.setFromAndTo = function (encodedId, from, to) {
    $(`#from_${encodedId}`).val(datetimeLocal(from));
    $(`#to_${encodedId}`).val(datetimeLocal(to));
}

var millisecondsInHour = 1000 * 3600;
var millisecondsInDay = millisecondsInHour * 24;

window.Date.prototype.AddDays = function (days) {
    let date = new Date(this.valueOf());
    date.setDate(date.getDate() + days);
    return date;
}

window.Date.prototype.AddHours = function (hours) {
    let newDate = new Date(this.valueOf());
    newDate.setHours(newDate.getHours() + hours);
    return newDate;
}
window.Data = function (to, from, type, encodedId) {
    return {
        "To": to,
        "From": from,
        "Type": type,
        "EncodedId": encodedId,
        "BarsCount": getBarsCount(encodedId),
        "Options": "IncludeTtl"
    };
}

//Initialization

window.initialize = function () {
    initializeSensorAccordion();
    initializeFileSensorEvents();
}

window.searchHistory = function (encodedId) {
    const {from, to} = getFromAndTo(encodedId);
    GetSensortInfo(encodedId).done(function (types) {
        if (Object.keys(types).length === 0)
            return;

        requestHistory(encodedId, historyAction, rawHistoryAction, types, Data(to, from, types.realType, encodedId));
    })
}

window.InitializeHistory = function () {
    let info = ($('[id^=meta_info_]')).attr('id');
    if (info === undefined)
        return;

    let encodedId = info.substring("meta_info_".length)
    let date = new Date();

    let historyPeriod = window.localStorage.getItem(`historyPeriod_${encodedId}`);
    if (historyPeriod != null) {
        $('#history_period').val(historyPeriod);

        if (historyPeriod === 'Custom') {
            let historyFrom = window.localStorage.getItem(`historyFrom_${encodedId}`);
            let from = isNaN(Date.parse(historyFrom)) ? date.AddDays(-1) : new Date(historyFrom + 'Z');

            let historyTo = new Date(window.localStorage.getItem(`historyTo_${encodedId}`));
            let to = isNaN(Date.parse(historyTo)) ? date.AddDays(1) : new Date(historyTo + 'Z');

            setFromAndTo(encodedId, from, to);
            searchHistory(encodedId);
        } else
            $('#history_period').trigger('change');
    } else {
        GetSensortInfo(encodedId).done(function (sensorInfo) {
            if (Object.keys(sensorInfo).length === 0)
                return;

            if (sensorInfo.realType === 0 && sensorInfo.plotType === 10) {
                $('#history_period').trigger('change');
                return;
            }
            
            if (isFileSensor(sensorInfo.realType))
                return;

            if (isTableHistorySelected(encodedId))
                initializeTable(encodedId, historyLatestAction, sensorInfo.realType, Data(date, date, sensorInfo.realType, encodedId), true)
            else if (isGraphAvailable(sensorInfo.realType)) 
                initializeGraph(encodedId, rawHistoryLatestAction, sensorInfo, Data(date, date, sensorInfo.realType, encodedId), true)
            else
                initializeTable(encodedId, historyLatestAction, sensorInfo.realType, Data(date, date, sensorInfo.realType, encodedId), true);
        });
    }
}


function initializeSensorAccordion() {
    InitializeHistory();
    InitializePeriodRequests();
    initializeTabLinksRequests();
}

function initializeFileSensorEvents() {
    $('[id^="button_view_"]').off("click", viewFile);
    $('[id^="button_view_"]').on("click", viewFile);

    $('[id^="button_download_"]').off("click", downloadFile);
    $('[id^="button_download_"]').on("click", downloadFile);
}

function downloadFile() {
    let encodedId = this.id.substring("button_download_".length);

    window.location.href = getFileAction + "?Selected=" + encodedId;
}

function viewFile() {
    let encodedId = this.id.substring("button_view_".length);
    let fileType = document.getElementById('fileType_' + encodedId).value;

    openFileInBrowser(encodedId, fileType, viewFileAction);
}

function initializeTabLinksRequests() {
    $('[id^="link_graph_"]').off("click").on("click", requestGraph);
    $('[id^="link_table_"]').off("click").on("click", requestTable);
}

function requestGraph() {
    let encodedId = this.id.substring("link_graph_".length);
    const {from, to} = getFromAndTo(encodedId);

    showBarsCount(encodedId);
    enableHistoryPeriod()
    GetSensortInfo(encodedId).done(function (types) {
        if (Object.keys(types).length === 0)
            return;

        let body = Data(to, from, types.realType, encodedId);
        initializeGraph(encodedId, rawHistoryAction, types, body);
    })
}

function requestTable() {
    let encodedId = this.id.substring("link_table_".length);
    const {from, to} = getFromAndTo(encodedId);

    hideBarsCount(encodedId);
    enableHistoryPeriod()
    GetSensortInfo(encodedId).done(function (types) {
        if (Object.keys(types).length === 0)
            return;

        let body = Data(to, from, types.realType, encodedId);
        initializeTable(encodedId, historyAction, types.realType, body);
    })
}

function InitializePeriodRequests() {
    $('[id^="button_export_csv_"]').off("click").on("click", exportCsv);
}


//Request methods

function requestHistory(encodedId, action, rawAction, types, reqData) {
    if (!isGraphAvailable(types.realType)) {
        if (isTableAvailable(types.realType))
            initializeTable(encodedId, action, types.realType, reqData);

        return;
    }

    if (isTableHistorySelected(encodedId)) {
        initializeTable(encodedId, action, types.realType, reqData);
    } else {
        initializeGraph(encodedId, rawAction, types, reqData);
    }
}

function exportCsv() {
    let encodedId = this.id.substring("button_export_csv_".length);
    const {from, to} = getFromAndTo(encodedId);

    GetSensortInfo(encodedId).done(function (types) {
        if (Object.keys(types).length === 0)
            return;

        window.location.href = exportHistoryAction + "?EncodedId=" + encodedId + "&Type=" + types.realType + "&addHiddenColumns=" + hiddenColumns.isVisible + "&From=" + from + "&To=" + to;
    })
}

function initializeTable(encodedId, tableAction, type, body, needFillFromTo = false) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: tableAction + "?EncodedId=" + encodedId + "&Type=" + type,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function (data) {
        $("#newValuesCount").empty();
        $("#tableHistoryRefreshButton").addClass("d-none");

        $(`#values_${encodedId}`).html(data);

        let noValuesElement = document.getElementById(`noTableValues_${encodedId}`);
        if (noValuesElement != null) {
            $('#no_data_' + encodedId).show();
            $('#noDataValues').removeClass('d-none');
        } else {
            $('#no_data_' + encodedId).hide();
            $('#noDataValues').addClass('d-none');

            if (needFillFromTo) {
                let to = getToDate();
                let from = new Date($(`#oldest_date_${encodedId}`).val());
                
                from.setMinutes(from.getMinutes() - from.getTimezoneOffset());
                $(`#from_${encodedId}`).val(datetimeLocal(from));
                $(`#to_${encodedId}`).val(datetimeLocal(to.getTime()));

                reloadHistoryRequest(from, to, body);
            }
        }

        $("#sensorHistorySpinner").addClass("d-none");
        $('#historyDataPanel').removeClass('hidden_element');
    });
}

function initializeGraph(encodedId, rawHistoryAction, sensorInfo, body, needFillFromTo = false) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: rawHistoryAction + "?EncodedId=" + encodedId + "&Type=" + sensorInfo.realType,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function (data) {
        $("#tableHistoryRefreshButton").addClass("d-none");
        $('#allColumnsButton').addClass("d-none");

        let parsedData = JSON.parse(data);
        if (parsedData.error === true)
            $('#points_limit').show();
        else
            $('#points_limit').hide()

        let values = parsedData.value.values;
        if (values.length === 0) {
            $('#no_data_' + encodedId).show();
            $('#noDataGraph').removeClass('d-none');
            $(`#graph_${encodedId}`).empty();
        } else {
            $('#no_data_' + encodedId).hide();
            $('#noDataGraph').addClass('d-none');

            if (needFillFromTo) {
                let from = new Date(values[0].receivingTime);
                let to = getToDate();

                $(`#from_${encodedId}`).val(datetimeLocal(from));
                $(`#to_${encodedId}`).val(datetimeLocal(to.getTime()));

                reloadHistoryRequest(from, to, body);
            }
            displayGraph(JSON.stringify(parsedData.value), sensorInfo, `graph_${encodedId}`, encodedId);
        }

        $("#sensorHistorySpinner").addClass("d-none");
        $('#historyDataPanel').removeClass('hidden_element');
    });
}

function reloadHistoryRequest(from, to, body) {
    let model = Data(to, from, body.Type, body.EncodedId);

    $.ajax({
        type: 'POST',
        url: reloadRequest,
        data: JSON.stringify(model),
        contentType: 'application/json',
        cache: false,
        async: true
    });
}


// Sub-methods

function isFileSensor(type) {
    return type === 6;
}

function isGraphAvailable(type) {
    return !(type === 3 || type === 6 || type === 8);
}

function isTableAvailable(type) {
    return type !== 6
}

function isTableHistorySelected(encodedId) {
    let el = $('#values_parent_' + encodedId);
    return el.hasClass("show");
}

function getToDate() {
    let now = new Date();

    now.setDate(now.getDate() + 1);

    return now;
}

function datetimeLocal(datetime) {
    const dt = new Date(datetime);

    if (isNaN(dt.getTime()))
       return (new Date()).toISOString().slice(0, 16);

    return dt.toISOString().slice(0, 16);
}


function hideBarsCount(encodedId) {
    $(`[id^="labelBarsCount_"]`).hide();
    $(`[id^="barsCount_"]`).hide();
}

function showBarsCount(encodedId) {
    $(`[id^="labelBarsCount_"]`).show();
    $(`[id^="barsCount_"]`).show();
}

function getBarsCount(encodedId) {
    let barsCount = $(`#barsCount_${encodedId}`).val();

    if (barsCount == "" || barsCount == undefined) {
        return setBarsCount(encodedId, 100);
    }
    if (barsCount > 1000) {
        return setBarsCount(encodedId, 1000);
    }
    if (barsCount < 1) {
        return setBarsCount(encodedId, 1);
    }

    return barsCount;
}

function setBarsCount(encodedId, count) {
    $(`#barsCount_${encodedId}`).val(count);

    return count
}


//Pagination

window.showPage = function (getPageAction, encodedId) {
    $('#nextPageButton').addClass('disabled');
    $('#prevPageButton').addClass('disabled');

    $.ajax({
        type: 'GET',
        url: getPageAction,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function (data) {
        $(`#values_${encodedId}`).html(data);
    });
}


//Journal

window.showNoData = function (data) {
    if (data.responseJSON.recordsTotal === 0) {
        $('#noDataPanel').removeClass('d-none');
        $('#noDataJournalPanel').addClass('d-none');
    } else {
        $('#noDataPanel').addClass('d-none');
        $('#noDataJournalPanel').removeClass('d-none');
    }
}

window.JournalTable = undefined;

window.DataTableColumnsNames = {
    Date: "Date",
    Path: "Path",
    Initiator: "Initiator",
    Type: "Type",
    Record: "Record"
};

window.JournalTemplate = (url, type) => {
    return {
        bAutoWidth: false,
        pageLength: 50,
        lengthMenu: [25, 50, 100, 300],
        processing: true,
        serverSide: true,
        order: [[0, 'desc']],
        ajax: {
            type: "POST",
            contentType: "application/json; charset=utf-8",
            url: url,
            data: function (d) {
                d.needSearchPath = type != NodeType.Sensor;
                return JSON.stringify(d);
            },
            complete: function (response) {
                showNoData(response)
            }
        }
    }
}

let nodeColumns = [
    {"name": DataTableColumnsNames.Date, "width": "10%"},
    {"name": DataTableColumnsNames.Path, "width": "20%"},
    {"name": DataTableColumnsNames.Initiator, "width": "5%"},
    {"name": DataTableColumnsNames.Record, "width": "55%"}
]

let sensorColumns = [
    {"name": DataTableColumnsNames.Date, "width": "10%"},
    {"name": DataTableColumnsNames.Initiator, "width": "10%"},
    {"name": DataTableColumnsNames.Record, "width": "85%"}
]

window.initializeJournal = function (type) {
    disableHistoryPeriod()
    hideBarsCount();

    if (JournalTable) {
        JournalTable.ajax.reload();
        return;
    }

    JournalTable = $('[id^="journal_table_"]').DataTable({
        columns: type === NodeType.Node ? nodeColumns : sensorColumns,
        ...JournalTemplate(getJournalPage, type)
    });
}

window.disableHistoryPeriod = function () {
    changeVisibility(true);
}

window.enableHistoryPeriod = function () {
    changeVisibility(false);
}

function changeVisibility(disable) {
    for (let el of $(`#datePickerFromTo label, #datePickerFromTo input, #datePickerFromTo button, #datePickerFromTo select`)) {
        el.disabled = disable;
        el.style.opacity = disable ? "0.5" : "1";
    }
}
