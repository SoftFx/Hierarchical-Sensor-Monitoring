//Add event listeners to buttons
function initializeDataHistoryRequests() {
    $(".accordion-button").on("click", function () {
        id = this.id;
        selected = id;

        totalCount = getCountForId(id);
        type = document.getElementById("sensor_type_" + id).value;

        if (type !== "3" && type !== "6" && type !== "7") {
            initializeGraph(id, type, totalCount, rawHistoryAction);
        }
        initializeHistory(id, totalCount, historyAction);
    });

    $('[id^="reload_"]').on("click",
        function () {
            id = this.id.substring("reload_".length, this.id.length);
            totalCount = getCountForId(id);
            type = document.getElementById("sensor_type_" + id).value;

            if (type !== "3" && type !== "6" && type !== "7") {
                initializeGraph(id, type, totalCount, rawHistoryAction);
            }
            initializeHistory(id, totalCount, historyAction);
        });

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

function getCountForId(id) {
    let inputCount = $('#inputCount_' + id).val();
    if (inputCount === undefined) {

        inputCount = 10;
    }

    return inputCount;
}

function data(path, totalCount) {
    return { "Path": path, "TotalCount": totalCount };
}

function initializeHistory(path, totalCount, historyAction) {
    if (totalCount == undefined)
        totalCount = 10;

    $.ajax({
        type: 'POST',
        data: JSON.stringify(data(path, totalCount)),
        url: historyAction,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        data = data.replace('{"value":"', ''); //fix sometime
        data = data.replace('"}', '');

        $(`#values_${path}`).empty();
        $(`#values_${path}`).append(data);
    });
}

function initializeGraph(id, type, totalCount, rawHistoryAction) {
    if (totalCount == undefined)
        totalCount = 10;

    $.ajax({
        type: 'POST',
        data: JSON.stringify(data(id, totalCount)),
        url: rawHistoryAction,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        let graphDivId = "graph_" + id;
        displayGraph(data, type, graphDivId, id);
    });
}