﻿window.getFromAndTo = function (encodedId) {
    let from = $(`#from_${encodedId}`).val();
    let to = $(`#to_${encodedId}`).val();

    if (to == "") {
        to = new Date().getTime() + 60000;
        $(`#to_${encodedId}`).val(datetimeLocal(to));
    }

    if (from == "") {
        from = to.AddDays(-1);
        $(`#from_${encodedId}`).val(datetimeLocal(from));
    }

    return { from, to };
}

var millisecondsInHour = 1000 * 3600;
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
window.Data = function (to, from, type, encodedId) {
    return { "To": to, "From": from, "Type": type, "EncodedId": encodedId, "BarsCount": getBarsCount(encodedId) };
}

//Initialization
{
    window.initialize = function() {
        initializeSensorAccordion();
        initializeFileSensorEvents();
    }

    window.searchHistory = function(encodedId) {
        let type = getTypeForSensor(encodedId);
        const { from, to } = getFromAndTo(encodedId);

        requestHistory(encodedId, historyAction, rawHistoryAction, type, Data(to, from, type, encodedId));
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

    function InitializeHistory() {
        let info = ($('[id^=meta_info_]')).attr('id');
        if (info === undefined) 
            return;
        
        let encodedId = info.substring("meta_info_".length)
        let type = getTypeForSensor(encodedId);
        let date = new Date();
        
        if (isFileSensor(type)) 
            return;
        
        if (isGraphAvailable(type)) {
            initializeGraph(encodedId, rawHistoryLatestAction, type, Data(date, date, type, encodedId), true);
        } else {
            initializeTable(encodedId, historyLatestAction, type, Data(date, date, type, encodedId), true);
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
       
        showBarsCount(encodedId);
        enableFromTo()
        initializeGraph(encodedId, rawHistoryAction, type, body);
    }

    function requestTable() {
        let encodedId = this.id.substring("link_table_".length);
        let type = getTypeForSensor(encodedId);
        const { from, to } = getFromAndTo(encodedId);
        let body = Data(to, from, type, encodedId);

        hideBarsCount(encodedId);
        enableFromTo()
        initializeTable(encodedId, historyAction, type, body);
    }

    function InitializePeriodRequests() {
        $('[id^="button_export_csv_"]').off("click").on("click", exportCsv);
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
        const { from, to } = getFromAndTo(encodedId);

        window.location.href = exportHistoryAction + "?EncodedId=" + encodedId + "&Type=" + type + "&From=" + from + "&To=" + to;
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
            $('#allColumnsButton').removeClass('d-none');
            $('#allColumnsButton')[0].innerText = 'Show all columns';

            $(`#values_${encodedId}`).html(data);

            let noValuesElement = document.getElementById(`noTableValues_${encodedId}`);
            if (noValuesElement != null) {
                $('#history_' + encodedId).hide();
                $('#no_data_' + encodedId).show();
                return;
            }

            $('#history_' + encodedId).show();
            $('#no_data_' + encodedId).hide();

            if (needFillFromTo) {
                let to = getToDate();
                let from = new Date($(`#oldest_date_${encodedId}`).val());
                from.setMinutes(from.getMinutes() - from.getTimezoneOffset());

                $(`#from_${encodedId}`).val(datetimeLocal(from));
                $(`#to_${encodedId}`).val(datetimeLocal(to.getTime()));

                reloadHistoryRequest(from, to, body);
            }

            $("#sensorHistorySpinner").addClass("d-none");
            $('#historyDataPanel').removeClass('hidden_element');
        });
    }

    function initializeGraph(encodedId, rawHistoryAction, type, body, needFillFromTo = false) {
        $.ajax({
            type: 'POST',
            data: JSON.stringify(body),
            url: rawHistoryAction + "?EncodedId=" + encodedId + "&Type=" + type,
            contentType: 'application/json',
            dataType: 'html',
            cache: false,
            async: true
        }).done(function (data) {
            $("#tableHistoryRefreshButton").addClass("d-none");
            $('#allColumnsButton').addClass("d-none");

            let parsedData = JSON.parse(data);

            if (parsedData.length === 0) {
                $('#history_' + encodedId).hide();
                $('#no_data_' + encodedId).show();
                return;
            }

            $('#history_' + encodedId).show();
            $('#no_data_' + encodedId).hide();

            if (needFillFromTo) {
                let from = new Date(parsedData[0].receivingTime);
                let to = getToDate();

                $(`#from_${encodedId}`).val(datetimeLocal(from));
                $(`#to_${encodedId}`).val(datetimeLocal(to.getTime()));

                reloadHistoryRequest(from, to, body);
            }

            displayGraph(data, type, `graph_${encodedId}`, encodedId);

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
}


// Sub-methods
{
    function isFileSensor(type) {
        return type === "6";
    }

    function isGraphAvailable(type) {
        return !(type === "3" || type === "6" || type === "8");
    }

    function isTableHistorySelected(encodedId) {
        let el = $('#values_parent_' + encodedId);
        return el.hasClass("show");
    }

    function getToDate() {
        let now = new Date();

        now.setFullYear(now.getFullYear() + 1);

        return now;
    }

    function datetimeLocal(datetime) {
        const dt = new Date(datetime);

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

    function getTypeForSensor(encodedId) {
        return $('#sensor_type_' + encodedId).first().val();
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

//Journal
{
    window.showNoData = function (data) {
        if (data.responseJSON.recordsTotal === 0) {
            $('#noDataPanel').removeClass('d-none');
            $('#noDataJournalPanel').addClass('d-none');
        }
        else {
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
   
    window.JournalTemplate = (url) => {
        return {
            bAutoWidth: false,
            pageLength: 50,
            lengthMenu: [25, 50, 100, 300 ],
            processing: true,
            serverSide: true,
            order: [[0, 'desc']],
            ajax: {
                type: "POST",
                contentType: "application/json; charset=utf-8",
                url: url,
                data: function (d) {
                    return JSON.stringify(d);
                },
                complete: function (response){
                    showNoData(response)
                }
            }
        }
    }
    
    let nodeColumns = [
        { "name": DataTableColumnsNames.Date , "width": "10%" },
        { "name": DataTableColumnsNames.Path , "width": "20%" },
        { "name": DataTableColumnsNames.Initiator , "width": "5%" },
        { "name": DataTableColumnsNames.Record , "width": "55%" }
    ]
    
    let sensorColumns = [
        { "name": DataTableColumnsNames.Date , "width": "10%" },
        { "name": DataTableColumnsNames.Initiator , "width": "10%" },
        { "name": DataTableColumnsNames.Record , "width": "85%" }
    ]
    
    window.initializeJournal = function(type) {
        disableFromTo()
        hideBarsCount();
        
        if (JournalTable) {
             JournalTable.ajax.reload();
             return;
        }

        JournalTable = $('[id^="journal_table_"]').DataTable({
            columns: type === NodeType.Node ? nodeColumns : sensorColumns,
            ...JournalTemplate(getJournalPage)
        });
    }

    window.disableFromTo = function () {
        changeVisibility('datePickerFromTo', true)
    }

    window.enableFromTo = function () {
        changeVisibility('datePickerFromTo');
    }

    function changeVisibility(containerId ,disable = false) {
        for (let el of $(`#${containerId} label, #${containerId} input, #${containerId} button`))
        {
            el.disabled = disable;
            el.style.opacity = disable ? "0.5" : "1";
        }
    }
}