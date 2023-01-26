﻿var millisecondsInHour = 1000 * 3600;
var millisecondsInDay = millisecondsInHour * 24;

Date.prototype.AddDays = function (days) {
    let date = new Date(this.valueOf());
    date.setDate(date.getDate() + days);
    return date;
}

Date.prototype.AddHours = function(hours) {
    let newDate = new Date(this.valueOf());
    newDate.setHours(newDate.getHours() + hours);
    return newDate;
}

function Data(to, from, type, encodedId) {
    return { "To": to, "From": from, "Type": type, "EncodedId": encodedId };
}

//Initialization
{
    window.initialize = function() {
        initializeSensorAccordion();
        initializeFileSensorEvents();
        initializeInfoLinks();
    }

    function initializeSensorAccordion() {
        $('[id^="collapse"]').off('show.bs.collapse', accordionClicked)
        $('[id^="collapse"]').on('show.bs.collapse', accordionClicked);

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

    function accordionClicked() {
        let encodedId = this.id.substring("collapse_".length);
        let type = getTypeForSensor(encodedId);
        const { from, to } = getFromAndTo(encodedId);
        if (isFileSensor(type)) {
            return;
        }
        if (isGraphAvailable(type)) {
            initializeGraph(encodedId, rawHistoryAction, type, Data(to, from, type, encodedId));
        } else {
            initializeTable(encodedId, historyAction, type, Data(to, from, type, encodedId));
        }
    }

    function initializeTabLinksRequests() {
        $('[id^="link_graph_"]').off("click").on("click", requestGraph);
        $('[id^="link_table_"]').off("click").on("click", requestTable);
    }

    function requestGraph() {
        let encodedId = this.id.substring("link_graph_".length);
        let type = getTypeForSensor(encodedId);
        const { from, to } = getFromAndTo(encodedId);
        let body = Data(to, from, type, encodedId);
        let action = isAllHistorySelected(encodedId) ? rawHistoryAllAction : rawHistoryAction;
        initializeGraph(encodedId, action, type, body);
    }

    function requestTable() {
        let encodedId = this.id.substring("link_table_".length);
        let type = getTypeForSensor(encodedId);
        const { from, to } = getFromAndTo(encodedId);
        let body = Data(to, from, type, encodedId);
        let action = isAllHistorySelected(encodedId) ? historyAllAction : historyAction;
        initializeTable(encodedId, action, type, body);
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
        let encodedId = this.id.substring("radio_hour_".length);
        let type = getTypeForSensor(encodedId);
        const to = new Date();
        const from = to.AddHours(-1);
        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
    }

    function requestHistoryDay() {
        let encodedId = this.id.substring("radio_day_".length);
        let type = getTypeForSensor(encodedId);
        const to = new Date();
        const from = to.AddDays(-1);
        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
    }

    function requestHistoryThreeDays() {
        let encodedId = this.id.substring("radio_three_days_".length);
        let type = getTypeForSensor(encodedId);
        const to = new Date();
        const from = to.AddDays(-3);
        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
    }

    function requestHistoryWeek() {
        let encodedId = this.id.substring("radio_week_".length);
        let type = getTypeForSensor(encodedId);
        const to = new Date();
        const from = to.AddDays(-7);
        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
    }

    function requestHistoryMonth() {
        let encodedId = this.id.substring("radio_month_".length);
        let type = getTypeForSensor(encodedId);
        const to = new Date();
        const from = to.AddDays(-30);
        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
    }

    function requestHistoryAll() {
        let encodedId = this.id.substring("radio_all_".length);
        let type = getTypeForSensor(encodedId);
        requestHistory(encodedId, historyAllAction, rawHistoryAllAction, type, {});
    }
}

//Request methods
{
    function requestHistory(encodedId, action, rawAction, type, reqData) {
        if (!isGraphAvailable(type)) {
            initializeTable(encodedId, action, type, reqData);
            return;
        }

        if (isTableHistorySelected(encodedId)) {
            initializeTable(encodedId, action, type, reqData);    
        } else {
            initializeGraph(encodedId, rawAction, type, reqData);
        }
    }

    function exportCsv() {
        let encodedId = this.id.substring("button_export_csv_".length);
        let type = getTypeForSensor(encodedId);
        if (isAllHistorySelected(encodedId)) {
            window.location.href = exportHistoryAllAction + "?EncodedId=" + encodedId + "&Type=" + type;
            return;
        }

        const { from, to } = getFromAndTo(encodedId);
        window.location.href = exportHistoryAction + "?EncodedId=" + encodedId + "&Type=" + type + "&From=" + from.toISOString() + "&To=" + to.toISOString();
    }

    function initializeTable(encodedId, tableAction, type, body) {
        $.ajax({
            type: 'POST',
            data: JSON.stringify(body),
            url: tableAction + "?EncodedId=" + encodedId + "&Type=" + type,
            contentType: 'application/json',
            dataType: 'html',
            cache: false,
            async: true
        }).done(function (data) {
            $(`#values_${encodedId}`).html(data);

            let noValuesElement = document.getElementById(`noTableValues_${encodedId}`);
            if (noValuesElement != null) {
                $('#history_' + encodedId).hide();
                $('#no_data_' + encodedId).show();
                return;
            }

            $('#history_' + encodedId).show();
            $('#no_data_' + encodedId).hide();
        });
    }

    function initializeGraph(encodedId, rawHistoryAction, type, body) {
        $.ajax({
            type: 'POST',
            data: JSON.stringify(body),
            url: rawHistoryAction + "?EncodedId=" + encodedId + "&Type=" + type,
            contentType: 'application/json',
            dataType: 'html',
            cache: false,
            async: true
        }).done(function (data) {
            if (JSON.parse(data).length === 0) {
                $('#history_' + encodedId).hide();
                $('#no_data_' + encodedId).show();
                return;
            }

            $('#history_' + encodedId).show();
            $('#no_data_' + encodedId).hide();
            let graphDivId = "graph_" + encodedId;

            displayGraph(data, type, graphDivId, encodedId);
        });
    }
}


// Sub-methods
{
    function isFileSensor(type) {
        return type === "6" || type === "7";
    }

    function isGraphAvailable(type) {
        return !(type === "3" || type === "6");
    }

    function isTableHistorySelected(encodedId) {
        let el = $('#values_parent_' + encodedId);
        return el.hasClass("show");
    }

    function isAllHistorySelected(encodedId) {
        return $('#radio_all_' + encodedId).is(":checked");
    }

    function getFromAndTo(encodedId) {
        let from = null;
        let to = null;
        if ($('#radio_hour_' + encodedId).is(":checked")) {
            to = new Date();
            from = to.AddHours(-1);
        }

        if ($('#radio_day_' + encodedId).is(":checked")) {
            to = new Date();
            from = to.AddDays(-1);
        }

        if ($('#radio_three_days_' + encodedId).is(":checked")) {
            to = new Date();
            from = to.AddDays(-3);
        }

        if ($('#radio_week_' + encodedId).is(":checked")) {
            to = new Date();
            from = to.AddDays(-7);
        }

        if ($('#radio_month_' + encodedId).is(":checked")) {
            to = new Date();
            from = to.AddDays(-30);
        }

        return { from, to };
    }


    function getCountForId(id) {
        let inputCount = $('#inputCount_' + id).val();
        if (inputCount === undefined) {
            inputCount = 10;
        }
        return inputCount;
    }

    function getTypeForSensor(encodedId) {
        return $('#sensor_type_' + encodedId).val();
    }    
}

//Pagination
{
    window.showPage = function(getPageAction, encodedId) {
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
}