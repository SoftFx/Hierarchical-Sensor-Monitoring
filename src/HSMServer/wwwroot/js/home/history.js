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

function Data(to, from, type, path) {
    return { "To": to, "From": from, "Type": type, "Path": path };
}

//Initialization
{
    //Add event listeners to buttons
    function initializeDataHistoryRequests() {
        $('[id^="collapse"]').off('show.bs.collapse').on('show.bs.collapse', function() {
            let path = this.id.substring("collapse_".length);
            let type = getTypeForSensor(path);
            let from = new Date();
            if (isGraphAvailable(type)) {
                initializeGraph(path, rawHistoryLatestAction, type, Data(from, from, type, path), true);
            } else {
                initializeTable(path, historyLatestAction, type, Data(from, from, type, path), true);
            }
        });

        InitializePeriodRequests();
        initializeTabLinksRequests();

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

    function initializeTabLinksRequests() {
        $('[id^="link_graph_"]').off("click").on("click", requestGraph);
        $('[id^="link_table_"]').off("click").on("click", requestTable);
    }

    function requestGraph() {
        let path = this.id.substring("link_graph_".length);
        let type = getTypeForSensor(path);
        const { from, to } = getFromAndTo(path);
        let body = Data(to, from, type, path);
        let action = isAllHistorySelected(path) ? rawHistoryAllAction : rawHistoryAction;
        initializeGraph(path, action, type, body, false);
    }

    function requestTable() {
        let path = this.id.substring("link_table_".length);
        let type = getTypeForSensor(path);
        const { from, to } = getFromAndTo(path);
        let body = Data(to, from, type, path);
        let action = isAllHistorySelected(path) ? historyAllAction : historyAction;
        initializeTable(path, action, type, body, false);
    }

    function InitializePeriodRequests() {

        $('[id^="radio_hour_"]').off("click").on("click", requestHistoryHour);

        $('[id^="radio_day_"]').off("click").on("click", requestHistoryDay);

        $('[id^="radio_three_days_"]').off("click").on("click", requestHistoryThreeDays);

        $('[id^="radio_week_"]').off("click").on("click", requestHistoryWeek);

        $('[id^="radio_month_"]').off("click").on("click", requestHistoryMonth);

        $('[id^="radio_all_"]').off("click").on("click", requestHistoryAll);

        $('[id^="button_export_csv_"]').off("click").on("click", exportCsv);

        $('[id^="button_delete_sensor_"]').off("click").on("click", deleteSensor);
    }

    function requestHistoryHour() {
        let path = this.id.substring("radio_hour_".length);
        let type = getTypeForSensor(path);
        const to = new Date();
        const from = to.AddHours(-1);
        requestHistory(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
    }

    function requestHistoryDay() {
        let path = this.id.substring("radio_day_".length);
        let type = getTypeForSensor(path);
        const to = new Date();
        const from = to.AddDays(-1);
        requestHistory(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
    }

    function requestHistoryThreeDays() {
        let path = this.id.substring("radio_three_days_".length);
        let type = getTypeForSensor(path);
        const to = new Date();
        const from = to.AddDays(-3);
        requestHistory(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
    }

    function requestHistoryWeek() {
        let path = this.id.substring("radio_week_".length);
        let type = getTypeForSensor(path);
        const to = new Date();
        const from = to.AddDays(-7);
        requestHistory(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
    }

    function requestHistoryMonth() {
        let path = this.id.substring("radio_month_".length);
        let type = getTypeForSensor(path);
        const to = new Date();
        const from = to.AddDays(-30);
        requestHistory(path, historyAction, rawHistoryAction, type, Data(to, from, type, path));
    }

    function requestHistoryAll() {
        let path = this.id.substring("radio_all_".length);
        let type = getTypeForSensor(path);
        requestHistory(path, historyAllAction, rawHistoryAllAction, type, {});
    }
}

//Request methods
{
    function requestHistory(path, action, rawAction, type, reqData) {
        if (!isGraphAvailable(type)) {
            initializeTable(path, action, type, reqData, false);
            return;
        }

        if (isTableHistorySelected(path)) {
            initializeTable(path, action, type, reqData, false);    
        } else {
            initializeGraph(path, rawAction, type, reqData, false);
        }
    }

    function exportCsv() {
        let path = this.id.substring("button_export_csv_".length);
        let type = getTypeForSensor(path);
        if (isAllHistorySelected(path)) {
            window.location.href = exportHistoryAllAction + "?Path=" + path + "&Type=" + type;
            return;
        }

        const { from, to } = getFromAndTo(path);
        window.location.href = exportHistoryAction + "?Path=" + path + "&Type=" + type + "&From=" + from.toISOString() + "&To=" + to.toISOString();
    }

    function initializeTable(path, tableAction, type, body, needSetRadio) {
        $.ajax({
            type: 'POST',
            data: JSON.stringify(body),
            url: tableAction + "?Path=" + path + "&Type=" + type,
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
            if (needSetRadio) {
                selectRadioForTable(path);
            }
        });
    }

    function initializeGraph(path, rawHistoryAction, type, body, needSetRadio) {
        $.ajax({
            type: 'POST',
            data: JSON.stringify(body),
            url: rawHistoryAction + "?Path=" + path + "&Type=" + type,
            contentType: 'application/json',
            dataType: 'html',
            cache: false,
            async: true
        }).done(function (data) {
            if (JSON.parse(data).length === 0) {
                $('#history_' + path).hide();
                $('#no_data_' + path).show();
                return;
            }

            $('#history_' + path).show();
            $('#no_data_' + path).hide();
            let graphDivId = "graph_" + path;
            if (needSetRadio) {
                selectAppropriateRadio(data, path);    
            }
            displayGraph(data, type, graphDivId, path);
        });
    }
}

function deleteSensor() {
    let path = this.id.substring("button_delete_sensor_".length);

    $.ajax({
        type: 'POST',
        url: removeSensorAction + "?Selected=" + path,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    });
}


// Sub-methods
{
    function selectRadioForTable(path) {
        let currentDate = new Date(Date.parse(new Date().toUTCString()));
        let oldestDate = getOldestDateFromTable(path);
        let difference = currentDate - oldestDate;
        selectRadioViaDifference(difference, path);
    }

    function getOldestDateFromTable(path) {
        let val = $('#oldest_date_' + path).val();
        return new Date(Date.parse(val));
    }

    function selectAppropriateRadio(data, path) {
        let parsedData = JSON.parse(data);
        let currentDate = new Date(Date.parse(new Date().toUTCString()));
        let firstDate = new Date(Date.parse(parsedData[0].time));
        let difference = currentDate - firstDate;
        selectRadioViaDifference(difference, path);
    }

    function selectRadioViaDifference(difference, path) {
        if (difference <= millisecondsInHour) {
            $('#radio_hour_' + path).prop("checked", true);
            return;
        }

        if (difference <= millisecondsInDay) {
            $('#radio_day_' + path).prop("checked", true);
            return;
        }

        if (difference <= 3 * millisecondsInDay) {
            $('#radio_three_days_' + path).prop("checked", true);
            return;
        }

        if (difference <= 7 * millisecondsInDay) {
            $('#radio_week_' + path).prop("checked", true);
            return;
        }

        if (difference <= 30 * millisecondsInDay) {
            $('#radio_month_' + path).prop("checked", true);
            return;
        }

        $('#radio_all_' + path).prop("checked", true);
    }

    function isGraphAvailable(type) {
        return !(type === "3" || type === "6" || type === "7");
    }

    function isTableHistorySelected(path) {
        let el = $('#values_parent_' + path);
        return el.hasClass("show");
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


    function getCountForId(id) {
        let inputCount = $('#inputCount_' + id).val();
        if (inputCount === undefined) {
            inputCount = 10;
        }
        return inputCount;
    }

    function getTypeForSensor(name) {
        return $('#sensor_type_' + name).val();
    }    
}