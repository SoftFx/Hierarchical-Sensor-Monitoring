import { convertToGraphData } from "./plotting";
import { pan } from "plotly.js/src/fonts/ploticon";
import { Colors, Plot, TimeSpanPlot } from "./plots";

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

export function Model(id, panelId, dashboardId, sensorId) {
    this.id = id;
    this.oldIndex = id;
    this.sensorId = sensorId;
    this.panelId = panelId;
    this.dashboardId = dashboardId;
    this.updateTimeout = undefined;
    this.requestTimeout = undefined;
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

window.insertSourcePlot = function (data, id, panelId, dashboardId) {
    let plot = convertToGraphData(JSON.stringify(data.values), data.sensorInfo, data.id, data.color, data.shape, data.chartType == 1);

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

    Plotly.addTraces(id, plot.getPlotData()).then(
        (data) => {
            if (plot instanceof TimeSpanPlot) {
                let y = [];
                for (let i of $(`#${id}`)[0].data)
                    y.push(...i.y);

                y = y.filter(element => {
                    return element !== null;
                })
                let timespanLayout = plot.getLayout(y);

                timespanLayout.margin = {
                    autoexpand: true,
                    l: 30,
                    r: 30,
                    t: 30,
                    b: 40,
                };

                timespanLayout.legend = {
                    y: 0,
                    orientation: "h",
                    yanchor: "bottom",
                    yref: "container"
                };

                timespanLayout.xaxis.automargin = true;

                Plotly.relayout(id, timespanLayout)
            }

            $('#emptypanel').hide()

            if (id === 'multichart')
                layoutUpdate['xaxis.autorange'] = true;

            Plotly.relayout(id, layoutUpdate)
        }
    );

    currentPanel[data.id] = new Model($(`#${id}`)[0].data.length - 1, panelId, dashboardId, data.sensorId);
}

window.addNewSourceHtml = function (data, id) {
    insertSourceHtml(data);
    insertSourcePlot(data, id);
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

    for (let i in currentPanel) {
        currentPanel[i].requestTimeout = setInterval(function () {
            $.ajax({
                type: 'get',
                url: window.location.pathname + '/SourceUpdate' + `/${currentPanel[i].panelId}/${i}`,
            }).done(function (data) {
                if (!$.trim(data))
                    return;

                if (data.newVisibleValues.length > 0) {
                    let plot = $(`#panelChart_${currentPanel[i].panelId}`)[0];

                    let correctId = 0;

                    for (let j of plot.data) {
                        if (j.id === i)
                            break;

                        correctId += 1;
                    }

                    let lastTime = new Date(0);

                    if (plot.data[correctId] !== undefined && plot.data[correctId].x.length > 0)
                        lastTime = new Date(plot.data[correctId].x.at(-1));

                    let prevData = plot.data[correctId];
                    let prevId = prevData.ids !== undefined && prevData.ids?.length !== 0 ? prevData.ids.at(-1) : undefined;
                    if (prevData.ids === undefined)
                        prevData.ids = [];
                    let redraw = false;

                    let x = [];
                    let y = [];
                    let customData = []
                    let isTimeSpan = data.isTimeSpan !== undefined && data.isTimeSpan === true;
                    for (let j of data.newVisibleValues) {
                        if (lastTime >= new Date(j.time))
                            continue;

                        if (isTimeSpan) {
                            let timespanValue = TimeSpanPlot.getTimeSpanValue(j);
                            customData.push(Plot.checkError(j) ? TimeSpanPlot.getTimeSpanCustomData(timespanValue, j) + '<br>' + j.comment : TimeSpanPlot.getTimeSpanCustomData(timespanValue, j))
                            x.push(j.time)
                            y.push(timespanValue === 'NaN' ? timespanValue : timespanValue.totalMilliseconds())
                        }
                        else {
                            if (prevId !== undefined && j.id === prevId) {
                                redraw = true;
                                prevData.x.pop();
                                prevData.y.pop();
                                prevData.customdata.pop();
                            }
                            x.push(j.time);
                            y.push(j.value);
                            prevData.ids.push(j.id)
                            let custom = j.value;
                            if (j.tooltip !== null)
                                custom += `<br>${j.tooltip}`;

                            customData.push(custom);
                        }

                    }

                    if (x.length >= 1 && y.length >= 1 && plot.data[correctId].x[0] === null) {
                        Plotly.update(plot, { x: [[]], y: [[]] }, { 'xaxis.autorange': true }, correctId)
                    }

                    if (redraw) {
                        prevData.x.push(...x)
                        prevData.y.push(...y)
                        prevData.customdata.push(...customData)
                        Plotly.deleteTraces(plot, correctId);
                        Plotly.addTraces(plot, prevData);
                        DefaultRelayout(plot);
                    }
                    else {
                        Plotly.extendTraces(plot, {
                            y: [y],
                            x: [x],
                            customdata: [customData]
                        }, [correctId], maxPlottedPoints).then(
                            (data) => {
                                if (isTimeSpan)
                                    TimespanRelayout(data);
                                else
                                    DefaultRelayout(data);
                            }
                        )
                    }
                }
            })
        }, 30000)
    }
}

function TimespanRelayout(data) {
    let y = [];
    for (let i of data.data)
        y.push(...i.y)

    y = y.filter(element => {
        return element !== null;
    })

    let layoutTicks = TimeSpanPlot.getLayoutTicks(y);
    let layoutUpdate = {
        'yaxis.ticktext': layoutTicks[1],
        'yaxis.tickvals': layoutTicks[0]
    }

    Plotly.relayout(data.id, layoutUpdate)
}

function DefaultRelayout(data) {
    let layoutUpdate = {
        'xaxis.range': getRangeDate(),
        'yaxis.autorange': true,
    }

    Plotly.relayout(data.id, layoutUpdate)
}

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
                    interact.snappers.grid({ x: 5, y: 5 })
                ],
                range: Infinity,
                relativePoints: [{ x: 0, y: 0 }]
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
        edges: { left: true, right: true, bottom: true, top: true },

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

                var update = {
                    'width': item.width(),
                    'height': item.height() - item.children('div').first().height()
                };

                Plotly.relayout(`panelChart_${event.target.id}`, update);
            }
        },
        modifiers: [
            interact.modifiers.restrictEdges({
                outer: 'parent'
            }),

            interact.modifiers.restrictSize({
                min: { width: 50, height: 100 }
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

window.initMultyichartCordinates = function (settings, values, id) {
    return new Promise(function (resolve, reject) {
        let dashboardPanels = $('#dashboardPanels');
        let width = dashboardPanels.width();
        let height = 1400;

        let currWidth = Number((settings.width * width).toFixed(5))
        let currHeight = Number((settings.height * height).toFixed(5))
        let transitionX = settings.x * width;
        let transitionY = settings.y * height;
        let panel = $(`#${id}`);

        if (panel.length === 0)
            reject();

        panel.width(currWidth)
            .height(currHeight)
            .css('transform', 'translate(' + transitionX + 'px, ' + transitionY + 'px)')
            .attr('data-x', transitionX)
            .attr('data-y', transitionY);

        resolve(transitionY + currHeight * 2);
    })
}

window.initMultichart = function (chartId, height = 300, showlegend = true, autorange = false) {
    return Plotly.newPlot(chartId, [], {
        hovermode: 'x',
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
            click: (plot) => customReset(plot, getRangeDate())
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
            name: updatedName,
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