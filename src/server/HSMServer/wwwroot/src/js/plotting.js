import {BarPLot, BoolPlot, DoublePlot, EnumPlot, IntegerPlot, Plot, TimeSpanPlot} from "./plots";

var serviceAliveIcon = {
    'width': 500,
    'height': 600,
    'path': 'M228.3 469.1L47.6 300.4c-4.2-3.9-8.2-8.1-11.9-12.4h87c22.6 0 43-13.6 51.7-34.5l10.5-25.2 49.3 109.5c3.8 8.5 12.1 14 21.4 14.1s17.8-5 22-13.3L320 253.7l1.7 3.4c9.5 19 28.9 31 50.1 31H476.3c-3.7 4.3-7.7 8.5-11.9 12.4L283.7 469.1c-7.5 7-17.4 10.9-27.7 10.9s-20.2-3.9-27.7-10.9zM503.7 240h-132c-3 0-5.8-1.7-7.2-4.4l-23.2-46.3c-4.1-8.1-12.4-13.3-21.5-13.3s-17.4 5.1-21.5 13.3l-41.4 82.8L205.9 158.2c-3.9-8.7-12.7-14.3-22.2-14.1s-18.1 5.9-21.8 14.8l-31.8 76.3c-1.2 3-4.2 4.9-7.4 4.9H16c-2.6 0-5 .4-7.3 1.1C3 225.2 0 208.2 0 190.9v-5.8c0-69.9 50.5-129.5 119.4-141C165 36.5 211.4 51.4 244 84l12 12 12-12c32.6-32.6 79-47.5 124.6-39.9C461.5 55.6 512 115.2 512 185.1v5.8c0 16.9-2.8 33.5-8.3 49.1z'
}

var serviceStatusIcon = {
    'width': 500,
    'height': 600,
    'path': 'M32 32c17.7 0 32 14.3 32 32V400c0 8.8 7.2 16 16 16H480c17.7 0 32 14.3 32 32s-14.3 32-32 32H80c-44.2 0-80-35.8-80-80V64C0 46.3 14.3 32 32 32zM160 224c17.7 0 32 14.3 32 32v64c0 17.7-14.3 32-32 32s-32-14.3-32-32V256c0-17.7 14.3-32 32-32zm128-64V320c0 17.7-14.3 32-32 32s-32-14.3-32-32V160c0-17.7 14.3-32 32-32s32 14.3 32 32zm64 32c17.7 0 32 14.3 32 32v96c0 17.7-14.3 32-32 32s-32-14.3-32-32V224c0-17.7 14.3-32 32-32zM480 96V320c0 17.7-14.3 32-32 32s-32-14.3-32-32V96c0-17.7 14.3-32 32-32s32 14.3 32 32z'
}

window.barGraphData = {
    plot: undefined,
    plotData: [],

    graph: {
        id: undefined,
        self: undefined,
        displayedPlots: ['bar']
    }
};

window.addBarPlot = function (name, isInit = false) {
    if (name === 'bar')
        Plotly.addTraces(barGraphData.graph.id, barGraphData.plot.getPlotData());
    else {
        let currPlot = new DoublePlot(barGraphData.plotData, name, name);
        Plotly.addTraces(barGraphData.graph.id, currPlot.getPlotData());
    }

    if (!isInit) {
        barGraphData.graph.displayedPlots.push(name);
        localStorage.setItem(barGraphData.graph.id, barGraphData.graph.displayedPlots)
    }

    Plotly.update(barGraphData.graph.id, {}, {hovermode: 'x'});
};

window.removeBarPlot = function (name, isInit = false) {
    let indexToDelete = undefined;
    let plots = barGraphData.graph.self._fullData;
    for (let i = 0; i < plots.length; i++) {
        if (plots[i].name === name) {
            indexToDelete = i;
            break;
        }
    }

    if (indexToDelete !== undefined) {
        Plotly.deleteTraces(barGraphData.graph.id, indexToDelete);
        if (!isInit) {
            barGraphData.graph.displayedPlots.splice(barGraphData.graph.displayedPlots.indexOf(name), 1);
            localStorage.setItem(barGraphData.graph.id, barGraphData.graph.displayedPlots)
        }
    }
}

window.displayGraph = function (graphData, sensorTypes, graphElementId, graphName) {
    barGraphData.graph.id = graphElementId
    barGraphData.graph.self = $(`#${graphElementId}`)[0];

    let plot = convertToGraphData(graphData, sensorTypes, graphName);
    let zoomData = getPreviousZoomData(graphElementId);

    var serviceButtonName = 'Show/Hide service status plot';
    var heartBeatButtonName = 'Show/Hide service alive plot';
    var config = {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToAdd: [
            getAddPlotButton(serviceButtonName, true, serviceStatusIcon, graphElementId, graphName),
            getAddPlotButton(heartBeatButtonName, false, serviceAliveIcon, graphElementId, graphName),
        ],
        modeBarButtonsToRemove: [
            'pan',
            'lasso2d',
            'pan2d',
            'select2d',
            'autoScale2d',
            'resetScale2d'
        ]
    }
    let layout;
    if (sensorTypes.plotType === 9 || sensorTypes.plotType === 7)
        layout = plot.getLayout();
    else {
        if (zoomData === undefined || zoomData === null)
            layout = plot.getLayout()
        else
            layout = createLayoutFromZoomData(zoomData, plot.getLayout());
    }

    Plotly.newPlot(graphElementId, plot.getPlotData(), layout, config);

    let savedPlots = localStorage.getItem(barGraphData.graph.id);
    if (savedPlots) {
        removeBarPlot('bar', true)
        removeBarPlot('min', true)
        removeBarPlot('max', true)
        removeBarPlot('mean', true)
        removeBarPlot('count', true)
        savedPlots.split(',').forEach((name) => {
            addBarPlot(name, true)
        })
    }

    let graphDiv = document.getElementById(graphElementId);
    graphDiv.on('plotly_relayout',
        function (eventData) {
            window.sessionStorage.setItem(graphElementId, JSON.stringify(eventData));
        });
}

function createLayoutFromZoomData(zoomData, layout) {
    let processedData = Object.values(JSON.parse(zoomData));

    layout.xaxis = {
        range: [processedData[0], processedData[1]]
    };

    layout.yaxis = {
        range: [processedData[2], processedData[3]]
    };

    layout.autosize = true;

    return layout;
}

function getPreviousZoomData(graphElementId) {
    return window.sessionStorage.getItem(graphElementId);
}

function convertToGraphData(graphData, sensorTypes, graphName) {
    let escapedData = JSON.parse(graphData);

    switch (sensorTypes.plotType) {
        case 0:
            return new BoolPlot(escapedData);
        case 1:
            return new IntegerPlot(escapedData);
        case 2:
            return new DoublePlot(escapedData);
        case 4:
            return new BarPLot(escapedData, graphName);
        case 5:
            return new BarPLot(escapedData, graphName);
        case 7:
            return new TimeSpanPlot(escapedData);
        case 9:
            if (sensorTypes.realType === 0)
                return new EnumPlot(escapedData, false)

            return new EnumPlot(escapedData, true);
        default:
            return undefined;
    }
}

function getAddPlotButton(name, isStatusService, icon, graphElementId, graphName) {
    return {
        name: name, //changing name doesn't work
        icon: icon,
        click: function (gd) {
            let graph = $(`#${graphElementId}`)[0];
            let plots = graph._fullData;
            if (plots.length > 1) {
                let indexToDelete = undefined;
                for (let i = 0; i < plots.length; i++) {
                    if (plots[i].name === name) {
                        indexToDelete = i;
                        break;
                    }
                }

                if (indexToDelete !== undefined)
                    Plotly.deleteTraces(graphElementId, indexToDelete);
            } 
            else {
                let {from, to} = getFromAndTo(graphName);
                let body = Data(to, from, 1, graphName)
                $.ajax({
                    type: 'POST',
                    data: JSON.stringify(body),
                    url: `${getSensorStatus}?isStatusService=${isStatusService}`,
                    contentType: 'application/json',
                    dataType: 'html',
                    cache: false,
                    async: true,
                    success: function (data) {
                        let escapedData = JSON.parse(data);
                        let ranges = graph._fullLayout.yaxis.range;
                        let heatPlot = new EnumPlot(escapedData, isStatusService)
                        Plotly.addTraces(graphElementId, heatPlot.getPlotData(name, ranges[0], ranges[1]));
                        Plotly.update(graphElementId, {}, {hovermode: 'x'});
                    }
                })
            }

            if (graph._fullData.length === 1)
                Plotly.update(graphElementId, {}, {hovermode: 'closest'});
        }
    }
}

export const ServiceStatus = {
    1: ['#FF0000', 'Stopped'],
    2: ['#BFFFBF', 'Start Pending'],
    3: ['#FD6464', 'Stop Pending'],
    4: ['#00FF00', 'Running'],
    5: ['#FFB403', 'Continue Pending'],
    6: ['#809EFF', 'Pause Pending'],
    7: ['#0314FF', 'Paused'],
    8: ['#666699', 'Timeout'],
    0: ['#000000', 'Unknown']
}
