import {BarPLot, BoolPlot, DoublePlot, EnumPlot, IntegerPlot, Plot, TimeSpanPlot} from "./plots";

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

window.displayGraph = function (graphData, graphType, graphElementId, graphName) {
    barGraphData.graph.id = graphElementId
    barGraphData.graph.self = $(`#${graphElementId}`)[0];
    let plot = convertToGraphData(graphData, graphType, graphName);
    let zoomData = getPreviousZoomData(graphElementId);
    var plotIcon = {
        'width': 500,
        'height': 600,
        'path': 'M352 96c0 14.3-3.1 27.9-8.8 40.2L396 227.4c-23.7 25.3-54.2 44.1-88.5 53.6L256 192h0 0l-68 117.5c21.5 6.8 44.3 10.5 68.1 10.5c70.7 0 133.8-32.7 174.9-84c11.1-13.8 31.2-16 45-5s16 31.2 5 45C428.1 341.8 347 384 256 384c-35.4 0-69.4-6.4-100.7-18.1L98.7 463.7C94 471.8 87 478.4 78.6 482.6L23.2 510.3c-5 2.5-10.9 2.2-15.6-.7S0 501.5 0 496V440.6c0-8.4 2.2-16.7 6.5-24.1l60-103.7C53.7 301.6 41.8 289.3 31.2 276c-11.1-13.8-8.8-33.9 5-45s33.9-8.8 45 5c5.7 7.1 11.8 13.8 18.2 20.1l69.4-119.9c-5.6-12.2-8.8-25.8-8.8-40.2c0-53 43-96 96-96s96 43 96 96zm21 297.9c32.6-12.8 62.5-30.8 88.9-52.9l43.7 75.5c4.2 7.3 6.5 15.6 6.5 24.1V496c0 5.5-2.9 10.7-7.6 13.6s-10.6 3.2-15.6 .7l-55.4-27.7c-8.4-4.2-15.4-10.8-20.1-18.9L373 393.9zM256 128a32 32 0 1 0 0-64 32 32 0 1 0 0 64z'
    }

    var serviceButtonName = 'Show/Hide service status plot';
    var heartBeatButtonName = 'Show/Hide service heart beat plot';
    var config = {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToAdd: [
            // getAddPlotButton(serviceButtonName, true, plotIcon, graphElementId, graphName),
            // getAddPlotButton(heartBeatButtonName, false, plotIcon, graphElementId, graphName),
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
    if (graphType === "9" || graphType === "7")
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

function convertToGraphData(graphData, graphType, graphName) {
    let escapedData = JSON.parse(graphData);

    switch (graphType) {
        case "0":
            return new BoolPlot(escapedData);
        case "1":
            return new IntegerPlot(escapedData);
        case "2":
            return new DoublePlot(escapedData);
        case "4":
            return new BarPLot(escapedData, graphName);
        case "5":
            return new BarPLot(escapedData, graphName);
        case "7":
            return new TimeSpanPlot(escapedData);
        case "9":
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
    0: ['#000000', 'Unknown']
}
