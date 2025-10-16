import {
    BarPLot,
    BoolPlot,
    Colors,
    DoublePlot,
    EnumPlot,
    IntegerPlot,
    ServiceAliveIcon,
    ServiceStatusIcon,
    TimeSpanPlot
} from "./plots";
import {VersionPlot} from "../ts/plots/version-plot";

export const serviceAlivePlotName = "ServiceAlive";
export const serviceStatusPlotName = "ServiceStatus";

export async function customReset(plot = undefined, xaxisRange = undefined, yaxisRange = undefined) {
    await Plotly.relayout(plot, await getLayout(plot, xaxisRange, yaxisRange));

    function getLayout(plot = undefined, xaxisRange = undefined, yaxisRange = undefined) {
        let currentPlot;

        let layout = {};

        if (plot.data.length === 1)
            currentPlot = plot.data[0];
        else
            plot.data.forEach(function (x) {
                if (x.type !== 'heatmap')
                    currentPlot = x;
            })

        if (currentPlot === undefined)
            return;

        let isPanelChart = plot.id.startsWith('panelChart');

        if (yaxisRange === undefined || yaxisRange === true)
            layout['yaxis.autorange'] = true;
        else
            layout['yaxis.range'] = yaxisRange

        if (!isPanelChart) {
            layout['xaxis.range'] = null;
            layout['xaxis.autorange'] = true;
            layout['yaxis.autorange'] = true;
        } else {
            layout['xaxis.range'] = [xaxisRange[0], !isPanelChart ? getMinRangeTo(currentPlot) : xaxisRange[1]];
        }

        return new Promise(function (resolve, reject) {
            resolve(layout)
        })
    }

    function getMinRangeTo(currentPlot) {
        if (currentPlot.x.length === 0)
            return xaxisRange[1];

        return moment.utc(Math.min(new Date(xaxisRange[1]), new Date(currentPlot.x.at(-1)))).toISOString();
    }
}

window.graphData = {
    plot: undefined,
    plotData: [],

    graph: {
        id: undefined,
        self: undefined,
        displayedPlots: ['bar']
    }
};

window.addPlot = function (name, isInit = false) {
    if (name === 'bar')
        Plotly.addTraces(graphData.graph.id, graphData.plot.getPlotData());
    else {
        let currPlot = new DoublePlot(graphData.plotData, name, name);
        Plotly.addTraces(graphData.graph.id, currPlot.getPlotData());
    }

    if (!isInit) {
        graphData.graph.displayedPlots.push(name);
        localStorage.setItem(graphData.graph.id, graphData.graph.displayedPlots)
    }

    Plotly.update(graphData.graph.id, {}, {hovermode: 'x'});
};

window.removePlot = function (name, isInit = false) {
    let indexToDelete = undefined;
    let plots = graphData.graph.self._fullData;
    for (let i = 0; i < plots.length; i++) {
        if (plots[i].name === name) {
            indexToDelete = i;
            break;
        }
    }

    if (indexToDelete !== undefined) {
        Plotly.deleteTraces(graphData.graph.id, indexToDelete);
        if (!isInit) {
            graphData.graph.displayedPlots.splice(graphData.graph.displayedPlots.indexOf(name), 1);
            localStorage.setItem(graphData.graph.id, graphData.graph.displayedPlots)
        }
    }
}

window.displayGraph = async function (data, sensorInfo, graphElementId, graphName) {
    graphData.graph.id = graphElementId;
    graphData.graph.self = $(`#${graphElementId}`)[0];

    let plot = convertToGraphData(data, sensorInfo, graphName);

    let config = {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToAdd: await getModeBarButtons(graphName, graphElementId),
        modeBarButtonsToRemove: [
            'pan',
            'lasso2d',
            'pan2d',
            'select2d',
            'autoScale2d',
            'resetScale2d'
        ],
        doubleClick: false
    }

    let layout = plot.getLayout();
    
    if (sensorInfo.plotType === 10) {
        layout.shapes = plot.shapes;
        layout.hovermode = 'closest';
        layout.hoverdistance = -1;
        layout.annotations = plot.annotations;
    }

    if (!layout.xaxis.autorange && layout.xaxis.range === undefined)
        layout.xaxis.autorange = true;

    await Plotly.newPlot(graphElementId, plot.getPlotData(), layout, config)
    await customReset($(`#${graphElementId}`)[0], getCurrentFromTo(graphName))
    

    if (plot.name === serviceAlivePlotName)
        config.modeBarButtonsToAdd.forEach(x => {
            if (x.name === "Show/Hide service alive plot")
                x.click = () => {};
        })

    if (plot.name === serviceStatusPlotName)
        config.modeBarButtonsToAdd.forEach(x => {
            if (x.name === "Show/Hide service status plot")
                x.click = () => {};
        })

    if (plot.name !== serviceAlivePlotName && plot.name !== serviceStatusPlotName)
        config.modeBarButtonsToAdd.forEach(x => {
            if (x.name === "Show/Hide service alive plot")
                x.click();
        })
    
    
    let savedPlots = localStorage.getItem(graphData.graph.id);
    if (savedPlots) {
        removePlot('bar', true)
        removePlot('min', true)
        removePlot('max', true)
        removePlot('mean', true)
        removePlot('count', true)
        savedPlots.split(',').forEach((name) => {
            addPlot(name, true)
        })
    }

    let graphDiv = document.getElementById(graphElementId);
    graphDiv.on('plotly_relayout',
        function (eventData) {
            window.sessionStorage.setItem(graphElementId, JSON.stringify(eventData));
        });

    graphDiv.on('plotly_doubleclick', function () {
        customReset(graphDiv, getCurrentFromTo(graphName))
    })
}

function getCurrentFromTo(id) {
    return [$(`#from_${id}`).val(), $(`#to_${id}`).val()]
}

function createLayoutFromZoomData(zoomData, layout) {
    let processedData = Object.values(JSON.parse(zoomData));

    layout.xaxis.range = processedData[0];
    layout.yaxis.range = processedData[1];
    layout.autosize = true;

    return layout;
}

export function convertToGraphData(graphData, sensorInfo, graphName, color = Colors.default, shape = undefined, asLine = false, range = undefined) {
    let escapedData = graphData.values;
    switch (sensorInfo.plotType) {
        case 0:
            return new BoolPlot(escapedData, sensorInfo.units, color, range);
        case 1:
            return new IntegerPlot(escapedData, sensorInfo.units, color, shape, range);
        case 2:
            return new DoublePlot(escapedData, graphName, 'value', sensorInfo.units, color, shape, range);
        case 4:
            return asLine ? new IntegerPlot(escapedData, sensorInfo.units, color, shape, range)
                : new BarPLot(escapedData, graphName, sensorInfo.units, color);
        case 5:
            return asLine ? new DoublePlot(escapedData, graphName, 'value', sensorInfo.units, color, shape, range)
                : new BarPLot(escapedData, graphName, sensorInfo.units, color);
        case 7:
            return new TimeSpanPlot(escapedData, sensorInfo.units, color, range);
        case 8:
            return new VersionPlot(escapedData, color, shape)
        case 9:
            return new DoublePlot(escapedData, graphName, 'value', sensorInfo.units, color, shape, range);
        case 10:
            if (sensorInfo.realType === 0)
                return new EnumPlot(escapedData, false, false)

            return new EnumPlot(escapedData, true, false);
        default:
            return undefined;
    }
}

function getBackgroundSensorId(id, isStatusService) {
    return $.ajax({
        type: 'GET',
        url: `${getBackgroundId}?currentId=${id}&isStatusService=${isStatusService}`,
        cache: false,
        async: false,
    });
}

function getDataForPlotButton(graphName, id, isStatusService) {
    let {from, to} = getFromAndTo(graphName);
    let body = Data(to, from, 1, id)
    return $.ajax({
        type: 'POST',
        data: JSON.stringify(body),
        url: `${getSensorStatus}?isStatusService=${isStatusService}`,
        contentType: 'application/json',
        cache: false,
        async: true,
    });
}

async function getModeBarButtons(id, graphId) {
    let modeBarButtons = [];
    let serviceButtonName = 'Show/Hide service status plot';
    let heartBeatButtonName = 'Show/Hide service alive plot';

    const status = await getBackgroundSensorId(id, true);
    const alive = await getBackgroundSensorId(id, false);

    if (!jQuery.isEmptyObject(status))
        modeBarButtons.push(addPlotButton(id, serviceButtonName, true, ServiceStatusIcon, graphId, status.id, status.path))

    if (!jQuery.isEmptyObject(alive)) {
        modeBarButtons.push(addPlotButton(id, heartBeatButtonName, false, ServiceAliveIcon, graphId, alive.id, alive.path))
    }

    modeBarButtons.push({
        name: 'resetaxes',
        _cat: 'resetscale',
        title: 'Reset axes',
        attr: 'zoom',
        val: 'reset',
        icon: Plotly.Icons.home,
        click: (plot) => customReset(plot, getCurrentFromTo(id))
    })

    return modeBarButtons;
}

function addPlotButton(graphName, name, isStatusService, icon, graphId, id, path) {
    return {
        name: name,
        icon: icon,
        click: function (gd) {
            addEnumPlot(graphId, graphName, id, isStatusService, path);
        }
    }
}

async function addEnumPlot(graphId, graphName, id, isStatusService, path) {
    let graph = $(`#${graphId}`)[0];
    let plots = graph.data;

    let currentName = isStatusService ? serviceStatusPlotName : serviceAlivePlotName;

    if (plots.map((x) => x.name).includes(currentName) && plots.length !== 1) {
        let indexToDelete = undefined;

        //let newShapes = [];
        //(graph.layout.shapes ?? []).reduce((_, currentValue) => { 
        //    if (currentValue.name !== currentName) {
        //        newShapes.push(currentValue)
        //    }
        //})

        let newShapes = (graph.layout.shapes ?? []).filter(shape =>
            shape.name !== currentName
        );

        
        for (let i = 0; i < plots.length; i++)
            if (plots[i].name === currentName) {
                indexToDelete = i;
                break;
            }

        if (indexToDelete !== undefined) {
            await Plotly.deleteTraces(graphId, indexToDelete);
            await Plotly.relayout(graphId, {shapes: newShapes, annotations: []});
        }
    } else {
        const result = await getDataForPlotButton(graphName, id, isStatusService);
        let yranges = graph.layout.yaxis.range;
        let xranges = graph.layout.xaxis.range;

        const currdata = graph.layout.yaxis.range;
        const average = (currdata[0] + currdata[1]) / 2;

        let heatPlot = new EnumPlot(result.values, isStatusService, true, average)
        let shapes = graph.layout.shapes ?? [];

        let updateLayout = {
            title: heatPlot.getTitle(path),
            hovermode: 'closest',
            'xaxis.range': xranges,
            shapes: [...heatPlot.shapes, ...shapes],
            annotations: heatPlot.annotations,
            hoverdistance: 50
        };

        await Plotly.addTraces(graphId, heatPlot.getPlotData(currentName), 0);
        await Plotly.update(graphId, {}, updateLayout);
    }

    if (graph._fullData.length === 1)
        await Plotly.update(graphId, {}, {
            hovermode: 'closest',
            title: {}
        });
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