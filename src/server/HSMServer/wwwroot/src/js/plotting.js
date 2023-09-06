import {
    BarPLot,
    BoolPlot,
    DoublePlot,
    EnumPlot,
    IntegerPlot,
    Plot,
    ServiceAliveIcon,
    ServiceStatusIcon,
    TimeSpanPlot
} from "./plots";


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
            getAddPlotButton(serviceButtonName, true, ServiceStatusIcon, graphElementId, graphName),
            getAddPlotButton(heartBeatButtonName, false, ServiceAliveIcon, graphElementId, graphName),
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

function getDataForPlotButton(graphName, isStatusService) {
    let {from, to} = getFromAndTo(graphName);
    let body = Data(to, from, 1, graphName)
    return $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: `${getSensorStatus}?isStatusService=${isStatusService}`,
        contentType: 'application/json',
        dataType: 'html',
        cache: false,
        async: true,
    });
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
            } else {
                getDataForPlotButton(graphName, isStatusService).done(function (data){
                    let escapedData = JSON.parse(data);
                    let ranges = graph._fullLayout.yaxis.range;
                    let heatPlot = new EnumPlot(escapedData, isStatusService)
                    let updateLayout = {
                        title: heatPlot.getTitle(),
                        hovermode: 'x'
                    };

                    Plotly.addTraces(graphElementId, heatPlot.getPlotData(name, ranges[0], ranges[1]));
                    Plotly.update(graphElementId, {}, updateLayout);
                });
            }

            if (graph._fullData.length === 1)
                Plotly.update(graphElementId, {}, {
                    hovermode: 'closest',
                    title: {}
                });
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
