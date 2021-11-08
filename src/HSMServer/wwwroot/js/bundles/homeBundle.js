//Mime types map
const mimeTypesMap = new Map();
mimeTypesMap.set('html', 'text/html');
mimeTypesMap.set('pdf', 'application/pdf');

//files functionality
function getMimeType(fileName) {
    let extension = getExtensionFromName(fileName);
    let fileType = mimeTypesMap.get(extension);
    if (fileType === undefined) {
        fileType = "text/html";
    }
    return fileType;
}

function getExtensionFromName(fileName) {
    let dotIndex = fileName.indexOf('.');
    if (dotIndex === -1) {
        return fileName;
    }
    return fileName.substring(dotIndex + 1, fileName.length);
}

function viewFile(path, fileName, viewFileAction) {
    let fileType = getMimeType(fileName);
    //var xhr = new XMLHttpRequest();
    //xhr.open('POST', viewFileAction, true);
    //xhr.responseType = 'blob';
    //xhr.onload = function () {
    //    let blob = new Blob([this.response], { type: fileType });
    //    console.log(blob);
    //    let url = window.URL.createObjectURL(blob);
    //    window.open(url);
    //}
    //xhr.send(JSON.stringify(fileData(product, path)));
    $.ajax({
        type: 'POST',
        url: viewFileAction + "?Selected=" + path,
        cache: false,
        contentType: "application/json",
        success: function (response) {
            if (fileType === undefined) {
                fileType = "text/html";
            }

            let blob = new Blob([response], { type: fileType });
            let url = window.URL.createObjectURL(blob);
            window.open(url);
        }
    });
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
function displayList(data) {
    $('[id^="list_"]').css('display', 'none');
    $('[id^="sensorData_"]').css('display', 'none');
    $('#noData').css('display', 'none');
    hideSensorInfoParentBlocks();

    let id = data.node.id;
    if (id.startsWith("sensor_")) {
        let path = id.substring("sensor_".length);
        let dataElement = $('#sensorData_' + path)[0];
        $('#sensorData_' + path).css('display', 'block');
        $('#sensorInfo_parent_' + path).css('display', 'block');
        let parent = dataElement.parentNode;
        parent.style.display = 'block';
        $('#' + path).click();
    } else {
        hideSensorInfoParentBlocks();
        let path = id;
        let listId = '#list_' + path;
        $(listId).css('display', 'block');
        showAllChildAccordionsById(listId);
    }
}

function showAllChildAccordionsById(id) {
    let element = $(id)[0];
    if (element != undefined) {
        let children = element.childNodes;
        console.log(children);
        children.forEach(ch => {
            if (ch.classList.contains('accordion')) {
                ch.style.display = 'block';
            }
        });    
    }
}

function hideSensorInfoParentBlocks() {
    $('[id^="sensorInfo_parent_"]').css("display", "none");
}
function displayGraph(graphData, graphType, graphElementId, graphName) {
    let convertedData = convertToGraphData(graphData, graphType, graphName);

    //console.log('converted graph data:', convertedData);
    let zoomData = getPreviousZoomData(graphElementId);
    if (zoomData === undefined || zoomData === null) {
        Plotly.newPlot(graphElementId, convertedData);    
    } else {
        let layout = createLayoutFromZoomData(zoomData);
        Plotly.newPlot(graphElementId, convertedData, layout);
    }

    let graphDiv = document.getElementById(graphElementId);
    graphDiv.on('plotly_relayout',
        function(eventData) {
            window.sessionStorage.setItem(graphElementId, JSON.stringify(eventData));
        });
}

function createLayoutFromZoomData(zoomData) {
    let processedData = Object.values(JSON.parse(zoomData));
    var layout = {
        xaxis : {
            range: [processedData[0], processedData[1]]
        },
        yaxis : {
            range: [processedData[2], processedData[3]]
        }
    };
    return layout;
}

function getPreviousZoomData(graphElementId) {
    return window.sessionStorage.getItem(graphElementId);
}

function convertToGraphData(graphData, graphType, graphName) {
    let escapedData = JSON.parse(graphData);

    let data;
    let deserialized;
    let timeList;
    switch (graphType) {
        case "0":
            data = getBoolData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "bar");
        case "1":
            data = getIntegersData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "scatter");
        case "2":
            data = getDoublesData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "scatter");
        case "4":
            deserialized = getDeserializedBarsData(escapedData);
            return createBarGraphData(deserialized, graphName);
        case "5":
            deserialized = getDeserializedBarsData(escapedData);
            return createBarGraphData(deserialized, graphName);
        default:
            return undefined;
    }
}

//Boolean 
{
    function getBoolData(escapedItems) {
        let bools = escapedItems.map(function(i) {
            let currentBoolean = JSON.parse(i.typedData).BoolValue === true;
            return currentBoolean ? 1 : 0;
        });

        return bools;
    }
}

//Simple plots: integer and double
{
    function getSimpleGraphData(timeList, dataList, chartType) {
        let data = [
            {
                x: timeList,
                y: dataList,
                type: chartType,
                //mode: "lines"
            }
        ];
        return data;
    }

    function getIntegersData(escapedItems) {
        let integers = escapedItems.map(function (i) {
            //let date = new Date();
            //console.log(i);
            //console.log(date - new Date(Date.parse(i.time)));
            return JSON.parse(i.typedData).IntValue;
        });

        return integers;
    }

    function getDoublesData(escapedItems) {
        let doubles = escapedItems.map(function (i) {
            return JSON.parse(i.typedData).DoubleValue;
        });

        return doubles;
    }

    function getTimeList(escapedItems) {
        return escapedItems.map(function (i) {
            return i.time;
        });
    }
}

//Boxplots
{

    function getDeserializedBarsData(escapedItems) {
        let deserialized = escapedItems.map(function (i) {
            return JSON.parse(i.typedData);
        });

        return deserialized;
    }

    function getTimeFromBars(escapedBarsData) {
        return escapedBarsData.map(function (d) {
            if (d.EndTime.startsWith("0001")) {
                return d.StartTime;
            }
            return d.EndTime;
        });
    }

    function createBarGraphData(escapedBarsData, graphName) {
        let max = getBarsMax(escapedBarsData);
        let min = getBarsMin(escapedBarsData);
        let median = getBarsMedian(escapedBarsData);
        let q1 = getBarsQ1(escapedBarsData);
        let q3 = getBarsQ3(escapedBarsData);
        let mean = getBarsMean(escapedBarsData);
        let timeList = getTimeFromBars(escapedBarsData);
        let data =
        [
            {
                "type": "box",
                "name": graphName,
                "q1": q1,
                "median": median,
                "q3": q3,
                "mean": mean,
                "lowerfence": min,
                "upperfence": max,
                "x": timeList
            }
        ];

        return data;
    }

    // Get numeric characteristics
    {
        function getBarsMin(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.Min;
            });
        }

        function getBarsMax(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.Max;
            });
        }

        function getBarsMedian(escapedBarsData) {
            let medians = new Array();

            escapedBarsData.map(function (d) {
                d.Percentiles.filter(p => p.Percentile === 0.5).map(function (p) {
                    medians.push(p.Value);
                });
            });

            return medians;
        }

        function getBarsQ1(escapedBarsData) {
            let q1s = new Array();

            escapedBarsData.map(function (d) {
                d.Percentiles.filter(p => p.Percentile === 0.25).map(function (p) {
                    q1s.push(p.Value);
                });
            });

            return q1s;
        }

        function getBarsQ3(escapedBarsData) {
            let q3s = new Array();

            escapedBarsData.map(function (d) {
                d.Percentiles.filter(p => p.Percentile === 0.75).map(function (p) {
                    q3s.push(p.Value);
                });
            });

            return q3s;
        }

        function getBarsMean(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.Mean;
            });
        }
    }
}


// plot type
function getPlotType(graphType) {
    // Use simple time series plot to display 
    if (graphType === 1 || graphType === 2) {
        return "scatter";
    }

    // Use box plot for box plots
    if (graphType === 4 || graphType === 5) {
        return "box";
    }

    // no plots for other types yet
    return undefined;
}
function initializeInfoLinks() {
    $('[id^="sensorInfo_link_"]').off("click").on("click", metaInfoLinkClicked);
}

function metaInfoLinkClicked() {
    let path = this.id.substring("sensorInfo_link_".length);
    if ($('#sensor_info_' + path).is(':empty')) {
        showMetaInfo(path);
    } else {
        hideMetaInfo(path);
    }
}

function showMetaInfo(path) {
    $.ajax({
        type: 'GET',
        url: getSensorInfoAction + "?Path=" + path,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        $('#sensor_info_' + path).empty().append(JSON.parse(data).value);
        setLinkText(path, "Hide meta info");
        initializeEditInfoButtons(path);
    });
}

function hideMetaInfo(path) {
    $('#sensor_info_' + path).empty();
    setLinkText(path, "Show meta info");
}

function setLinkText(path, text) {
    let link = document.getElementById('sensorInfo_link_' + path);
    link.textContent = text;
}

function initializeEditInfoButtons(path) {
    $('#editInfo_' + path).on("click", editInfoButtonClick);
    $('#revertInfo_' + path).on("click", revertInfoClick);
    $('#saveInfo_' + path).on("click", saveInfoClick);
}

function editInfoButtonClick() {
    let path = this.id.substring("editInfo_".length);
    $('#interval_' + path).removeAttr("disabled");
    $('#description_' + path).removeAttr("disabled");
    $('#saveInfo_' + path).removeAttr("disabled");
    $('#revertInfo_' + path).removeAttr("disabled");
}

function revertInfoClick() {
    let path = this.id.substring("revertInfo_".length);
    reloadInfo(path);
}

function saveInfoClick() {
    let path = this.id.substring('saveInfo_'.length);
    let description = getDescription(path);
    let interval = getInterval(path);
    let body = Info(description, interval, path);
    saveSensorInfo(body);
}

function saveSensorInfo(body) {
    $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: updateSensorInfoAction,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true
    }).done(function () {
        reloadInfo(body.EncodedPath);
    });
}

function Info(description, updatePeriod, encodedPath) {
    return { "Description": description, "ExpectedUpdateInterval": updatePeriod, "EncodedPath": encodedPath };
}

function getDescription(path) {
    return $('#description_' + path).val();
}

function getInterval(path) {
    return $('#interval_' + path).val();
}

function reloadInfo(path) {
    let link = document.getElementById('sensorInfo_link_' + path);
    link.click();
    link.click();
}
function initializeTree() {
    $('#jstree').jstree({
        "core": {
            "themes": {
                "name": "proton",
                'responsive' : true
            }
        },
        "contextmenu": {
            "items": function ($node) {
                var tree = $("#jstree").jstree(true);

                return {
                    "Delete": {
                        "separator_before": false,
                        "separator_after": false,
                        "label": "Delete",
                        "action": function (obj) {

                            //modal
                            $('#modalDeleteLabel').empty();
                            $('#modalDeleteLabel').append('Remove node');
                            $('#modalDeleteBody').empty();
                            $('#modalDeleteBody').append('Do you really want to remove "' + $node.text + '" node?');

                            var modal = new bootstrap.Modal(document.getElementById('modalDelete'));
                            modal.show();

                            //modal confirm
                            $('#confirmDeleteButton').on('click', function () {
                                modal.hide();

                                $.ajax({
                                    type: 'POST',
                                    url: removeNode + '?Selected=' + $node.id,
                                    dataType: 'html',
                                    contentType: 'application/json',
                                    cache: false,
                                    async: true
                                }).done(function () {
                                    //tree.delete_node($node);

                                    $('#list_' + $node.id).remove();
                                    $('#noData').css('display', 'block');
                                });                               
                            });

                            $('#closeDeleteButton').on('click', function () {
                                modal.hide();
                            });
                        }
                    }
                }
            }
        },
        "plugins": ["state", "contextmenu", "themes", "wholerow"]
    });
    //$('#jstree').jstree();

    $('#updateTime').empty();
    $('#updateTime').append('Update Time: ' + new Date().toUTCString());

    initializeClickTree();
}

function initializeClickTree() {
    $('#jstree').on('activate_node.jstree', function (e, data) {
        if (data == undefined || data.node == undefined || data.node.id == undefined)
            return;
        displayList(data);
    });
}