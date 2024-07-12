import {convertToGraphData} from "./plotting";
import {TimeSpanPlot, ErrorColorPlot} from "./plots";
import {Panel} from "../ts/dashboard.panel";
import {DashboardStorage} from "../ts/dashboard/dashboard.storage";
import {formObserver} from "./nodeData";
import {SiteHelper} from "../ts/services/site-helper";
import {VersionPlot} from "../ts/plots/version-plot";

const updateDashboardInterval = 120000; // 2min
export const dashboardStorage = new DashboardStorage();


window.addObserve = function(q){
    formObserver.addFormToObserve(q);
}

window.manualCheckBoundaries = function (){
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

export const currentPanel = {};
export const plotColorDelay = 1000;

export function Model(id, panelId, dashboardId, sensorId, range = undefined) {
    this.id = id;
    this.oldIndex = id;
    this.sensorId = sensorId;
    this.panelId = panelId;
    this.dashboardId = dashboardId;
    this.updateTimeout = undefined;
    this.requestTimeout = undefined;
    this.range = range;
}

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
    });
}

function checkForYRange(plot) {
    if ($('#multichart').length !== 0 &&
        plot instanceof ErrorColorPlot &&
        !(plot instanceof TimeSpanPlot))
        $('#y-range-settings').show()
    else
        $('#y-range-settings').hide()
}

export function insertSourcePlot (data, id, panelId, dashboardId, range = undefined) {
    let plot = convertToGraphData(JSON.stringify(data.values), data.sensorInfo, data.id, data.color, data.shape, data.chartType == 1, range);

    checkForYRange(plot)

    let layoutUpdate = {
        'xaxis.visible': true,
        'xaxis.type': 'date',
        'xaxis.autorange': false,
        'xaxis.range': getRangeDate(),
        'yaxis.visible': true,
        'yaxis.title.text': data.sensorInfo.units,
        'yaxis.title.font.size': 14,
        'yaxis.title.font.color': '#7f7f7f',
    }

    if (plot.autoscaleY !== true && plot.autoscaleY !== undefined)
        layoutUpdate['yaxis.range'] = plot.autoscaleY;

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

    let plotData = plot.getPlotData();
    let panel = dashboardStorage.getPanel(panelId)
    if (panel)
        panel.lastUpdateTime = new Date(plotData[0].x.at(-1));

    currentPanel[data.id] = new Model($(`#${id}`)[0].data.length - 1, panelId, dashboardId, data.sensorId, range);
    currentPanel[data.id].isTimeSpan = plot instanceof TimeSpanPlot;
    
    return plotData;
}

window.addNewSourceHtml = function (data, id) {
    insertSourceHtml(data);
    insertSourcePlot(data, id, undefined, undefined, multichartRange);
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
        ondrop: function (event) {
            getPlotSourceView(event.relatedTarget.id).then(
                (data) => addNewSourceHtml(data, 'multichart'),
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
                if (panel.settings.isSingleMode){
                    let children = item.children();
                    let title = children[0].getBoundingClientRect();
                    let data = item.find('table')[0].getBoundingClientRect();
                    let childrenHeight = title.height + data.height;
                    let childrenWidth = data.width;
                    target.style.height = childrenHeight + 'px';
                    if (childrenWidth > event.rect.width)
                        target.style.width = childrenWidth + 'px';
                }
                else {
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
    if (currentPanel[id] === undefined)
        return;

    if (currentPanel[id].updateTimeout !== undefined)
        clearTimeout(currentPanel[id].updateTimeout);

    currentPanel[id].updateTimeout = setTimeout(updatePlotSource, plotColorDelay, name, color, property, shape, showProduct, id);
}

window.getCurrentPlotInDashboard = function (id) {
    return currentPanel[id]
}

window.syncIndexes = function () {
    let sources = $('#sources li');
    let plot = $('#multichart')[0].data;

    for (let i = 0; i < sources.length; i++) {
        currentPanel[`${sources[i].id.substring('source_'.length, sources[i].id.length)}`].oldIndex = i;
    }

    for (let i = 0; i < plot.length; i++) {
        currentPanel[`${plot[i].id}`].id = i;
    }
}

window.initPanel = async function (id, settings, ySettings, values, lastUpdate, panelSourceType, unit) {
   await dashboardStorage.initPanel(id, settings, ySettings, values, lastUpdate, panelSourceType, unit);
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
    let updatedName = defaultLabelUpdate(currentPanel[id].oldIndex, name)

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
    }).done(function (response) {
        if (response !== '') {
            Plotly.deleteTraces('multichart', currentPanel[id].id);
            insertSourcePlot(response, 'multichart');
            syncIndexes();
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

        if (currentPanel[id] !== undefined)
            Plotly.restyle('multichart', layoutUpdate, currentPanel[id].id)

        currentPanel[id].updateTimeout = undefined;
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