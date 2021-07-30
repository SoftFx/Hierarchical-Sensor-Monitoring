//Add event listeners to buttons
function initializeDataHistoryRequests() {
    $(".accordion-button").on("click", function () {
        id = this.id;
        selected = id;
        splitResults = id.split('_');
        product = splitResults[0];
        path = id.substring(product.length + 1);
        totalCount = getCountForId(id);
        type = document.getElementById("sensor_type_" + id).value;

        if (type !== "3" && type !== "6" && type !== "7") {
            initializeGraph(id, product, path, type, totalCount, rawHistoryAction);
        }
        initializeHistory(product, path, totalCount, historyAction);
    });

    $('[id^="reload_"]').on("click",
        function () {
            id = this.id.substring("reload_".length, this.id.length);
            splitResults = id.split('_');
            product = splitResults[0];
            path = id.substring(product.length + 1);
            totalCount = getCountForId(id);

            type = document.getElementById("sensor_type_" + id).value;
            //var graphVisible = $('#graph_' + product + "_" + path).css("display") !== "none";
            //if (graphVisible) {
            //    initializeGraph(noNumberId, product, path, type, totalCount, rawHistoryAction);
            //}
            //else {
            //    initializeHistory(product, path, totalCount, historyAction);
            //}
            if (type !== "3" && type !== "6" && type !== "7") {
                initializeGraph(id, product, path, type, totalCount, rawHistoryAction);
            }
            initializeHistory(product, path, totalCount, historyAction);
        });

    //$('[id^="butHtmlon_graph_"]').on("click",
    //    function () {
    //        id = this.id.substring("button_graHtmlh_★ Url".length, this.id.length);
    //        splitResults = id.split('_');
    //        product = splitResults[0];
    //        path = id.substring(product.length + 1, id.length - 2);
    //        totalCount = $('#inputCount_' + id).val();
    //        type = splitResults[splitResults.length - 1];
    //        noNumberId = id.substring(0, id.length - 2);

    //        initializeGraph(noNumberId, product, path, type, totalCount, rawHistoryAction);
    //    });

    $('[id^="button_view"]').on("click",
        function () {
            id = this.id.substring("button_view".length, this.id.length);

            //console.log(id);
            let splitRes = id.split('_');
            let product = splitRes[1];
            let fileName = splitRes[splitRes.length - 1];
            let path = id.substring(product.length + 2, id.length - fileName.length - 1);

            //window.open(getFileAction + "?Product=" + product + "&Path=" + path, '_blank');
            viewFile(product, path, fileName, viewFileAction);
        }
    );

    $('[id^="button_download"]').on("click",
        function () {
            id = this.id.substring("button_download".length, this.id.length);

            let splitRes = id.split('_');
            let product = splitRes[1];
            let fileName = splitRes[splitRes.length - 1];
            let path = id.substring(product.length + 2, id.length - fileName.length - 1);

            window.location.href = getFileAction + "?Product=" + product + "&Path=" + path;
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

function data(product, path, totalCount) {
    return { "Path": path, "Product": product, "TotalCount": totalCount };
}

function initializeHistory(product, path, totalCount, historyAction) {
    if (totalCount == undefined)
        totalCount = 10;

    $.ajax({
        type: 'POST',
        data: JSON.stringify(data(product, path, totalCount)),
        url: historyAction,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        data = data.replace('{"value":"', ''); //fix sometime
        data = data.replace('"}', '');

        $(`#values_${product}_${path}`).empty();
        $(`#values_${product}_${path}`).append(data);
    });
}

function initializeGraph(id, product, path, type, totalCount, rawHistoryAction) {
    if (totalCount == undefined)
        totalCount = 10;

    $.ajax({
        type: 'POST',
        data: JSON.stringify(data(product, path, totalCount)),
        url: rawHistoryAction,
        dataType: 'html',
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (data) {
        let graphDivId = "graph_" + id;
        displayGraph(data, type, graphDivId, path);
    });
}