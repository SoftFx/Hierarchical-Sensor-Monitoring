import {convertToGraphData, customReset} from "./plotting";
import {TimeSpanPlot, ErrorColorPlot} from "./plots";
import {Panel} from "../ts/dashboard.panel";
import {DashboardStorage} from "../ts/dashboard/dashboard.storage";
import {formObserver} from "./nodeData";
import {SiteHelper} from "../ts/services/site-helper";
import {VersionPlot} from "../ts/plots/version-plot";
import Plotly from "plotly.js";

const updateDashboardInterval = 120000; // 2min
export const dashboardStorage = new DashboardStorage();


window.addObserve = function (q) {
    formObserver.addFormToObserve(q);
}

window.manualCheckBoundaries = function () {
    SiteHelper.ManualCheckDashboardBoundaries();
}

window.getRangeDate = function () {
    let period = $('#from_select').val();

    let currentDate = new Date(new Date(Date.now()).toUTCString());
    let lastDate = currentDate.toISOString()
    let newDate
    switch (period) {
        case "00:30:00":
            newDate = currentDate.setMinutes(currentDate.getMinutes() - 30)
            break
        case "01:00:00":
            newDate = currentDate.setHours(currentDate.getHours() - 1)
            break
        case "03:00:00":
            newDate = currentDate.setHours(currentDate.getHours() - 3)
            break
        case "06:00:00":
            newDate = currentDate.setHours(currentDate.getHours() - 6)
            break
        case "12:00:00":
            newDate = currentDate.setHours(currentDate.getHours() - 12)
            break
        case "1.00:00:00":
            newDate = currentDate.setDate(currentDate.getDate() - 1)
            break
        case "3.00:00:00":
            newDate = currentDate.setDate(currentDate.getDate() - 3)
            break
        case "7.00:00:00":
            newDate = currentDate.setDate(currentDate.getDate() - 7)
            break
        case "30.00:00:00":
            newDate = currentDate.setDate(currentDate.getDate() - 30)
            break

        default:
            newDate = currentDate.setHours(currentDate.getHours() - 6)
    }

    return [new Date(newDate).toISOString(), lastDate]
}

function defaultLabelUpdate(id, name) {
    let sources = $('#sources').find('li');
    if (sources.length <= id)
        return name;

    let row = sources[id];
    let label = $(row).find('input[id^="name_input"]')
    let property = $(row).find(`select[id^='property_']`).find(':selected');
    let sensorNameDefault = $(row).find('input[id^="name_default"]').val();

    if (label.length === 0)
        return name;

    if (label.val().startsWith(sensorNameDefault)) {
        label.val(sensorNameDefault + ` (${property.text()})`)

        return label.val();
    }

    return name;
}

export function getPlotSourceView(id) {
    let showProduct = $(`input[name='ShowProduct']`).is(':checked');

    return new Promise(function (resolve, reject) {
        $.ajax({
            type: 'GET',
            url: `${window.location.pathname}/${id}?showProduct=${showProduct}`
        }).done(function (data) {
            if (data.error === undefined)
                return resolve(data);
            else
                return reject(data.error)
        }).fail(function (data) {
            reject(data.responseText)
        })
    })
}

export const multichartPanel = {};
export const plotColorDelay = 1000;

window.insertSourceHtml = function (data) {
    let sources = $('#sources');

    $.ajax({
        type: 'POST',
        url: getSourceSettings,
        data: JSON.stringify(data),
        contentType: 'application/json',
        cache: false,
        async: true
    }).done(function (result) {
        sources.append(result);
        setMultichartRemoveListeners();
    });
}

window.setMultichartRemoveListeners = () => {
    $('[id^=removeSource_]').off('click').on('click', function(e) {
        removeSource(this.id.substring('removeSource_'.length));
    })

    $('[id^=removeAllSources_]').off('click').on('click', function(e) {
        deleteAllPlots();
    })
}

function removeSource(sourceId) {
    showConfirmationModal(
        `Removing source`,
        `Do you really want to remove selected source?`,
        function () {
            deletePlot(sourceId);
        }
    )
}

const getMultichartTraceIndex = (sourceId) => {
    let multichartrtData = $('#multichart')[0].data;
    for (let i = 0; i < multichartrtData.length; i++) {
        if (multichartrtData[i].id === sourceId)
        {
           return i;
        }
    }
    
    return undefined;
} 

function deletePlot(sourceId) {
    let sourceIndex = getMultichartTraceIndex(sourceId);
    
    if (sourceIndex !== undefined)
        $.ajax({
            type: 'delete',
            url: window.location.pathname + '/' + sourceId
        }).done(function (response) {
            Plotly.deleteTraces('multichart', sourceIndex).then(
                (data) => {
                    if (data.data.length === 0) {
                        Plotly.relayout('multichart', { 'xaxis.visible': false, 'yaxis.visible': false });
                        $('#emptypanel').show();
                    }

                    $(`#source_${sourceId}`).remove();

                    if ($('[id^="source"] li').length === 0)
                        $('#y-range-settings').show();
                }
            )
        }).fail(function (response) {
            showToast(response.responseText);
        });
}

function deleteAllPlots() {
    const sourceIds = Array.from($('#multichart')[0].data, (val, index) => val.id);
    
    showConfirmationModal(
        `Removing sources`,
        `Do you really want to remove all panel sources?`,
        function () {
            $.ajax({
                type: 'put',
                url: window.location.pathname + '/DeleteSources',
                data: JSON.stringify({
                    ids: sourceIds
                }),
                contentType: 'application/json'
            }).done((response) => {
                let multichartLength = $('#multichart')[0].data.length;
                Plotly.deleteTraces('multichart', Array.from({ length: multichartLength }, (_, index) => index));

                for (let i of sourceIds) {
                    $(`#source_${i}`).remove();
                }

                $('#y-range-settings').show();
                $('#emptypanel').show();
                Plotly.relayout('multichart', { 'xaxis.visible': false, 'yaxis.visible': false });
            })
        }
    );
}

function removeAllSources() {
    showConfirmationModal(
        `Removing sources`,
        `Do you really want to remove all panel sources?`,
        function () {
            $('#sources li').each(function (idx, li) {
                let source = $(li);

                let idAttr = source.attr('id');
                let sourceId = idAttr.substring('source_'.length, idAttr.length);

                deletePlot(sourceId);
            })
        }
    );
}

function checkForYRange(plot) {
    if ($('#multichart').length !== 0 &&
        plot instanceof ErrorColorPlot &&
        !(plot instanceof TimeSpanPlot))
        $('#y-range-settings').show()
    else
        $('#y-range-settings').hide()
}

export async function createChart(chartId, data, layout, config) {
    return Plotly.newPlot(chartId, data, layout, config)
}

export function insertSourcePlot(data, id, panelId, dashboardId, range = undefined) {
    let plot = convertToGraphData(JSON.stringify({values: data.values}), data.sensorInfo, data.id, data.color, data.shape, data.chartType == 1, range);

    checkForYRange(plot)

    if (data.values.length === 0) {
        plot.x = [null]
        plot.y = [null];
    }

    plot.id = data.id;
    plot.name = data.displayLabel;
    plot.mode = 'lines+markers';
    plot.hovertemplate = `${plot.name}, %{customdata}<extra></extra>`
    plot.showlegend = true;
    plot['marker']['color'] = data.color;

    if (plot.type === 'scatter')
        plot.type = 'scattergl';

    let plotData = plot.getPlotData();
    let panel = dashboardStorage.getPanel(panelId)
    if (panel)
        panel.lastUpdateTime = new Date(plotData[0].x.at(-1));

    return plotData;
}

window.addNewSourceHtml = async function (data, id) {
    insertSourceHtml(data);
    var plotData = insertSourcePlot(data, id, undefined, undefined, multichartRange)[0];
    
    await Plotly.addTraces('multichart', plotData);
    await Plotly.relayout('multichart', { 'xaxis.visible': true, 'yaxis.visible': true });

    $('#emptypanel').hide();
}

export function initDropzone() {
    window.dragMoveListener = dragMoveListener

    window.interact('.dropzone').dropzone({
        overlap: 0.75,

        checker: function (
            dragEvent,
            event,
            dropped,
            dropzone,
            dropzoneElement,
            draggable,
            draggableElement
        ) {
            let dropzoneRect = dropzoneElement.getBoundingClientRect();
            let targetRect = $(`#${dragEvent.target.id}.cloned`)[0].getBoundingClientRect();

            if (targetRect.x > dropzoneRect.x && targetRect.y > dropzoneRect.y &&
                targetRect.width + targetRect.x < dropzoneRect.width + dropzoneRect.x &&
                targetRect.height + targetRect.y < dropzoneRect.height + dropzoneRect.y)
                return true;

            return false;
        },

        ondropactivate: function (event) {
            event.target.classList.add('drop-active')
        },
        ondragenter: function (event) {
            var draggableElement = event.relatedTarget
            var dropzoneElement = event.target

            dropzoneElement.classList.add('drop-target')
            draggableElement.classList.add('can-drop')
        },
        ondragleave: function (event) {
            event.target.classList.remove('drop-target')
            event.relatedTarget.classList.remove('can-drop')
        },
        ondrop: async function (event) {
            getPlotSourceView(event.relatedTarget.id).then(
                (data) => {
                    addNewSourceHtml(data, 'multichart')
                },
                (error) => showToast(error)
            )
        },
        ondropdeactivate: function (event) {
            event.target.classList.remove('drop-active')
            event.target.classList.remove('drop-target')
        }
    })

    window.interact('.drag-drop')
        .draggable({
            inertia: false,
            modifiers: [],
            autoScroll: true,
            listeners: {
                start(event) {
                    var interaction = event.interaction;
                    if (interaction.pointerIsDown && !interaction.interacting() && event.currentTarget.getAttribute('clonable') != 'false') {
                        var original = event.currentTarget;
                        var clone = event.currentTarget.cloneNode(true);
                        clone.setAttribute('clonable', 'false');
                        clone.style.position = 'fixed';
                        let rect = original.getBoundingClientRect();
                        clone.style.left = rect.left + 'px';
                        clone.style.top = rect.top + 'px';
                        clone.classList.add('cloned');
                        clone.classList.add('d-flex');

                        document.body.append(clone);
                    }
                },
                move: dragMoveListener,
                end: showEventInfo
            }
        })
}

const maxPlottedPoints = 1500;
window.initDashboard = function () {
    const currentRange = getRangeDate();
    const layoutUpdate = {
        'xaxis.range': currentRange
    }
    for (let i of $('[id^="panelChart_"]'))
        Plotly.relayout(i, layoutUpdate)

    const interactPanelResize = window.interact('.resize-draggable')
    const interactPanelDrag = window.interact('.name-draggable')
    addDraggable(interactPanelDrag)
    addResizable(interactPanelResize)

    dashboardStorage.initUpdateRequests()
}

window.addPanelToStorage = function (id, settings, lastUpdate) {
    dashboardStorage.addPanel(new Panel(id, settings), lastUpdate)
};

window.disableDragAndResize = function () {
    interact('.resize-draggable').options.resize.enabled = false;
    interact('.name-draggable').options.drag.enabled = false;
}

window.enableDragAndResize = function () {
    interact('.resize-draggable').options.resize.enabled = true;
    interact('.name-draggable').options.drag.enabled = true;
}

function addDraggable(interactable) {
    interactable.draggable({
        modifiers: [
            interact.modifiers.restrictRect({
                restriction: '#dashboardPanels',
                endOnly: true
            }),
            interact.modifiers.snap({
                targets: [
                    interact.snappers.grid({x: 5, y: 5})
                ],
                range: Infinity,
                relativePoints: [{x: 0, y: 0}]
            }),
        ],
        autoScroll: true,

        listeners: {
            move: dragMoveListenerPanel
        }
    })
}

function addResizable(interactable) {
    interactable.resizable({
        edges: {left: true, right: true, bottom: true, top: true},

        listeners: {
            move(event) {
                var target = event.target
                var x = (parseFloat(target.getAttribute('data-x')) || 0)
                var y = (parseFloat(target.getAttribute('data-y')) || 0)

                target.style.width = event.rect.width + 'px'
                target.style.height = event.rect.height + 'px'

                x += event.deltaRect.left
                y += event.deltaRect.top

                target.style.transform = 'translate(' + x + 'px,' + y + 'px)'

                target.setAttribute('data-x', x)
                target.setAttribute('data-y', y)

                if (changesCounter === 0)
                    changesCounter += 1;

                let item = $(target)

                let panel = dashboardStorage.getPanel(event.target.id);
                if (panel.settings.isSingleMode) {
                    let children = item.children();
                    let title = children[0].getBoundingClientRect();
                    let data = item.find('table')[0].getBoundingClientRect();
                    let childrenHeight = title.height + data.height;
                    let childrenWidth = data.width;
                    target.style.height = childrenHeight + 'px';
                    if (childrenWidth > event.rect.width)
                        target.style.width = childrenWidth + 'px';
                } else {
                    var update = {
                        'width': item.width(),
                        'height': item.height() - item.children('div').first().height()
                    };

                    Plotly.relayout(`panelChart_${event.target.id}`, update);
                }
            }
        },
        modifiers: [
            interact.modifiers.restrictEdges({
                outer: 'parent'
            }),

            interact.modifiers.restrictSize({
                min: {width: 50, height: 100}
            })
        ],
    })
}

window.updateSource = function (name, color, property, shape, showProduct, id) {
    if (multichartPanel[id] === undefined)
        multichartPanel[id] = {};
    
    if (multichartPanel[id].updateTimeout !== undefined)
        clearTimeout(multichartPanel[id].updateTimeout);

    multichartPanel[id].updateTimeout = setTimeout(updatePlotSource, plotColorDelay, name, color, property, shape, showProduct, id);
}

window.initPanel = async function (id, settings, ySettings, values, lastUpdate, dashboardId, panelSourceType, unit, range = undefined) {
    await dashboardStorage.initPanel(id, settings, ySettings, values, lastUpdate, dashboardId, panelSourceType, unit, range);
}


window.multiChartPanelInit = async (values, sourceType, unit = '', height = 300, range = true) => {
    const data = [];
    values.forEach(function (x) {
        data.push(insertSourcePlot(x, `multichart`, undefined, undefined, range)[0]);
    })

    let layout = {
        hovermode: 'closest',
        hoverdistance: 1,
        dragmode: 'zoom',
        autosize: true,
        height: height,
        margin: {
            // @ts-ignore
            autoexpand: true,
            l: 30,
            r: 30,
            t: 30,
            b: 40,
        },
        showlegend: true,
        legend: {
            y: 0,
            x: 0,
            orientation: "h",
            yanchor: "bottom",
            // @ts-ignore
            yref: "container",
        },
        xaxis: {
            type: 'date',
            visible: true,
            autorange: true,
            automargin: true,
            range: getRangeDate(),
            title: {
                //text: 'Time',
                font: {
                    family: 'Courier New, monospace',
                    size: 18,
                    color: '#7f7f7f'
                }
            },
            rangeslider: {
                visible: false
            }
        },
        yaxis: {
            categoryorder: 'trace',
            visible: true,
            title: {
                text: unit,
                font: {
                    size: 14,
                    color: '#7f7f7f'
                }
            },
            tickmode: "auto",
            // @ts-ignore
            ticktext: [],
            // @ts-ignore
            tickvals: [],
            tickfont: {
                size: 10
            },
            // @ts-ignore
            automargin: 'width+right'
        },
    }

    if (sourceType === 'TimeSpan') {
        const ticks = TimeSpanPlot.getYaxisTicks(data);
        layout.yaxis.tickmode = 'array';
        layout.yaxis.ticktext = ticks.ticktext;
        layout.yaxis.tickvals = ticks.tickvals;
    }

    if (sourceType === 'Version') {
        const ticks = VersionPlot.getYaxisTicks(data);
        layout.yaxis.tickmode = 'array';
        layout.yaxis.ticktext = ticks.ticktext;
        layout.yaxis.tickvals = ticks.tickvals;
        layout.yaxis.categoryorder = ticks.categoryorder;
    }

    const config = {
        responsive: true,
        displaylogo: false,
        modeBarButtonsToRemove: [
            'pan',
            'lasso2d',
            'pan2d',
            'select2d',
            'autoScale2d',
            'autoScale2d',
            'resetScale2d'
        ],
        modeBarButtonsToAdd: [
            {
                name: 'resetaxes',
                _cat: 'resetscale',
                title: 'Reset axes',
                attr: 'zoom',
                val: 'reset',
                icon: Plotly.Icons.home,
                click: (plot) => $(plot).trigger('plotly_doubleclick')
            }],
        doubleClick: false
    }

    await createChart(`multichart`, data, layout, config)
    $('#multichart').on('plotly_doubleclick', async function(){
        await customReset($(`#multichart`)[0])
    })

    if (values.length === 0) {
        $('#emptypanel').show();
    }
}


window.initMultichart = function (chartId, height = 300, showlegend = true, autorange = false, yaxisRange = true) {
    return Plotly.newPlot(chartId, [], {
            hovermode: 'closest',
            hoverdistance: 1,
            dragmode: 'zoom',
            autosize: true,
            height: height,
            margin: {
                autoexpand: true,
                l: 30,
                r: 30,
                t: 30,
                b: 40,
            },
            showlegend: showlegend,
            legend: {
                y: 0,
                x: 0,
                orientation: "h",
                yanchor: "bottom",
                yref: "container",
            },
            xaxis: {
                type: 'date',
                autorange: autorange,
                automargin: true,
                range: getRangeDate(),
                title: {
                    //text: 'Time',
                    font: {
                        family: 'Courier New, monospace',
                        size: 18,
                        color: '#7f7f7f'
                    }
                },
                visible: false,
                rangeslider: {
                    visible: false
                }
            },
            yaxis: {
                visible: false,
                automargin: 'width+right'
            }
        },
        {
            responsive: true,
            displaylogo: false,
            modeBarButtonsToRemove: [
                'pan',
                'lasso2d',
                'pan2d',
                'select2d',
                'autoScale2d',
                'autoScale2d',
                'resetScale2d'
            ],
            modeBarButtonsToAdd: [
                {
                    name: 'resetaxes',
                    _cat: 'resetscale',
                    title: 'Reset axes',
                    attr: 'zoom',
                    val: 'reset',
                    icon: Plotly.Icons.home,
                    click: (plot) => $(plot).trigger('plotly_doubleclick')
                }],
            doubleClick: autorange ? 'reset+autosize' : autorange
        });
}

function showEventInfo(event) {
    let id = event.target.id;
    $(`#${id}.cloned`).remove();
}

function updatePlotSource(name, color, property, shape, showProduct, id) {
    let updatedName = defaultLabelUpdate(getMultichartTraceIndex(id), name)
    
    $.ajax({
        processData: false,
        type: 'put',
        contentType: 'application/json',
        url: window.location.pathname + '/' + id,
        data: JSON.stringify({
            label: updatedName,
            color: color,
            property: property,
            shape: shape
        })
    }).done(async function (response) {
        let traceId = getMultichartTraceIndex(id);
        if (response !== '') {
            await Plotly.deleteTraces('multichart', traceId);
            let plotData = insertSourcePlot(response, 'multichart')[0];
            await Plotly.addTraces('multichart', plotData, traceId);
        }

        if (showProduct)
            updatedName = $(`#productName_${id}`).text() + updatedName;

        let layoutUpdate = {
            'hovertemplate': `${updatedName}, %{customdata}<extra></extra>`,
            'line.color': color,
            'marker.color': color,
            'line.shape': shape,
            name: updatedName
        }

        if (multichartPanel[id] !== undefined)
            await Plotly.restyle('multichart', layoutUpdate, traceId)

        multichartPanel[id].updateTimeout = undefined;
    }).fail(function (response) {
        showToast(response.responseText)
    })
}

function dragMoveListener(event) {
    let id = event.target.id;
    var target = $(`#${id}.cloned`)[0];

    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)
}

function dragMoveListenerPanel(event) {
    var target = event.target.parentNode.parentElement;

    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)

    if (changesCounter === 0)
        changesCounter += 1;
}