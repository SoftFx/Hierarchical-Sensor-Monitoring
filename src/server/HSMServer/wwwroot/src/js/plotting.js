﻿import {
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

    let config = {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToAdd: getModeBarButtons(graphName, graphElementId),
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

function getBackgroundSensorId(id, isStatusService){
    return $.ajax({
        type: 'GET',
        url: `${getBackgroundId}?currentId=${id}&isStatusService=${isStatusService}`,
        cache: false,
        async: false,
    });
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

function getModeBarButtons(id, graphId){
    let modeBarButtons = [];
    let serviceButtonName = 'Show/Hide service status plot';
    let heartBeatButtonName = 'Show/Hide service alive plot';
  
    $.when(getBackgroundSensorId(id, true), getBackgroundSensorId(id, false)).done(function(status, alive){
        if(!jQuery.isEmptyObject(status[0]))
            modeBarButtons.push(addPlotButton(serviceButtonName, true, ServiceStatusIcon, graphId, status[0].id, status[0].path))

        if(!jQuery.isEmptyObject(alive[0]))
            modeBarButtons.push(addPlotButton(heartBeatButtonName, false, ServiceAliveIcon, graphId, alive[0].id, alive[0].path))
    });

    return modeBarButtons;
}

function addPlotButton(name, isStatusService, icon, graphId, id, path){
    return {
        name: name,
        icon: icon,
        click: function (gd) {
            let graph = $(`#${graphId}`)[0];
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
                    Plotly.deleteTraces(graphId, indexToDelete);
            } else {
                getDataForPlotButton(id, isStatusService).done(function (data){
                    let escapedData = JSON.parse(data);
                    let ranges = graph._fullLayout.yaxis.range;
                    let heatPlot = new EnumPlot(escapedData, isStatusService)
                    let updateLayout = {
                        title: heatPlot.getTitle(path),
                        hovermode: 'x'
                    };

                    Plotly.addTraces(graphId, heatPlot.getPlotData(name, ranges[0], ranges[1]));
                    Plotly.update(graphId, {}, updateLayout);
                });
            }

            if (graph._fullData.length === 1)
                Plotly.update(graphId, {}, {
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
