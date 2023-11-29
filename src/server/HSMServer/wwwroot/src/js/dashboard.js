import {convertToGraphData} from "./plotting";
import {pan} from "plotly.js/src/fonts/ploticon";

export function getPlotSourceView(id) {
    return new Promise(function (resolve, reject) {
        $.ajax({
            type: 'GET',
            url: `${window.location.pathname}/${id}`
        }).done(function (data) {
            if (data.errorMessage === undefined)
                return resolve(data);
            else
                return reject(data.errorMessage)
        }).fail(function (data){
            reject(data.responseText)
        })
    })
}

export const currentPanel = {};
export const plotColorDelay = 1000;

export function Model(id, panelId, dashboardId) {
    this.id = id;
    this.panelId = panelId;
    this.dashboardId = dashboardId;
    this.updateTimeout = undefined;
    this.requestTimeout = undefined;
}

window.insertSourceHtml = function (data) {
    let sources = $('#sources');
    let text = `<li id=${'source_' + data.id} class="d-flex flex-wrap list-group-item my-1 align-items-center justify-content-between"
                                    style="border-top-width: 1px;
                                           border-radius: 5px;"
                                    >
                                    <div class="d-flex flex-grow-1">
                                        <div class="d-flex flex-column" style="flex-grow: 10">
                                            <div class="d-flex mx-1 align-items-center" style="flex-grow: 10">
                                                <label class="me-1">Label:</label>
                                                <input id=${'name_input_' + data.id} class="form-control" value="${data.label}" type="text" style="flex-grow: 10"></input>
                                                <input id=${'color_' + data.id} type="color" value=${data.color} class="form-control form-control-color mx-1 ="></input>
                                            </div>
                                            <div class="d-flex align-items-center">
                                                <span id=${'redirectToHome_' + data.sensorId} class="ms-1 redirectToHome" style="color: grey;font-size: x-small;text-decoration-line: underline;cursor: pointer;">
                                                    ${data.path}
                                                </span>
                                            </div>
                                        </div>
                                        <div class="d-flex justify-content-center">
                                             <button id=${'deletePlot_' + data.id} class="btn" type="button" style="color: red">
                                                <i class="fa-solid fa-xmark"></i>
                                             </button>
                                        </div>
                                    </div>
                                </li>`

    sources.html(function(n, origText) {
        return origText + text;
    });
}

window.insertSourcePlot = function (data, id, panelId, dashboardId) {
    let plot = convertToGraphData(JSON.stringify(data.values), data.sensorInfo, data.id, data.color);
    plot.id = data.id;
    plot.name = data.label;
    plot.mode = 'lines';
    plot.hovertemplate = `${plot.name}, %{customdata}<extra></extra>`
    plot.showlegend = true;
    Plotly.addTraces(id, plot.getPlotData());

    let updateLayout = {
        'yaxis.title' : {
            text: data.sensorInfo.units,
            font: {
                family: 'Courier New, monospace',
                size: 18,
                color: '#7f7f7f'
            }
        }
    }
    Plotly.relayout(id, updateLayout)
    currentPanel[data.id] = new Model($(`#${id}`)[0].data.length - 1, panelId, dashboardId);
}

window.addNewSourceHtml = function (data, id){
    insertSourceHtml(data);
    insertSourcePlot(data, id);
}

export function initDropzone(){
    window.dragMoveListener = dragMoveListener
    
    window.interact('.dropzone').dropzone({
        overlap: 0.75,

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
            if (currentPanel[event.relatedTarget.id] !== undefined)
                return;

            let sources = $('#sources');
            let color = getRandomColor();
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
            inertia: true,
            modifiers: [],
            autoScroll: true,
            listeners: {
                start (event) {
                    event.target.style.position = "fixed";
                },
                move: dragMoveListener,
                end: showEventInfo
            }
        })
}

window.initDashboard = function () {
    const interactPanelResize = window.interact('.resize-draggable')
    const interactPanelDrag = window.interact('.name-draggable')
    addDraggable(interactPanelDrag)
    addResizable(interactPanelResize)
    for (let i in currentPanel){
        currentPanel[i].requestTimeout = setInterval(function() {
            $.ajax({
                type: 'get',
                url: window.location.pathname + '/SourceUpdate' + `/${currentPanel[i].panelId}/${i}`,
            }).done(function(data){
                if (!$.trim(data))
                    return;
                
                if (data.newVisibleValues.length > 0) {
                    let plot = $(`#panelChart_${currentPanel[i].panelId}`)[0];
                    let correctId = 0;

                    for(let j of plot.data){
                        if (j.id === i)
                            break;

                        correctId += 1;
                    }
                    
                    let lastTime = new Date(plot.data[correctId].x.at(-1));
                    let x = [];
                    let y = [];
                    let customData = []
                    for(let j of data.newVisibleValues){
                        if (lastTime > new Date(j.time))
                            continue;
 
                        x.push(j.time);
                        y.push(j.value);
                        customData.push(j.value);
                    }

                    Plotly.extendTraces(plot, {
                        y: [y],
                        x: [x],
                        customdata: [customData]
                    }, [correctId])
                }
            })
        }, 30000)
    }
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
                relativePoints: [ { x: 0, y: 0 } ]
            }),
        ],
        autoScroll: true,

        listeners: {
            move: dragMoveListenerPanel,

            end(event) {
                var textEl = event.target.querySelector('p')

                //textEl && (textEl.textContent =
                //    'moved a distance of ' +
                //    (Math.sqrt(Math.pow(event.pageX - event.x0, 2) +
                //        Math.pow(event.pageY - event.y0, 2) | 0))
                //        .toFixed(2) + 'px')
            }
        }
    })
}

function addResizable(interactable){
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


                var update = {
                    width: event.rect.width,
                    height: event.rect.height
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

window.updateSource = function (name, color, id){
    if (currentPanel[id] === undefined)
        return;

    if (currentPanel[id].updateTimeout !== undefined)
        clearTimeout(currentPanel[id].updateTimeout);

    currentPanel[id].updateTimeout = setTimeout(updatePlotSource, plotColorDelay, name, color, id);
}

window.getCurrentPlotInDashboard = function (id) {
    return currentPanel[id]
}

window.updateCurrentPlotsIds = function (idToCompare, id) {
    delete currentPanel[id];
    
    for (let item in currentPanel) {
        if (currentPanel[item].id >= idToCompare)
            currentPanel[item].id = currentPanel[item].id - 1;
    }
}

window.initMultyichartCordinates = function(settings, values, id){
    return new Promise(function(resolve, reject){
        let dashboardPanels = $('#dashboardPanels');
        let width = dashboardPanels.width();
        let height = dashboardPanels.height();

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
        
        resolve();
    })
}

window.initMultichart = function (chartId, height = 300, showlegend = true) {
    return Plotly.newPlot(chartId, [], {
        hovermode: 'x',
        dragmode: 'zoom',
        autosize: true,
        height: height,
        margin: {
            l: 30,
            r: 30,
            t: 10,
            b: 0,
        },
        showlegend: showlegend,
        legend: {
            "orientation": "h",
            traceorder: "grouped"
        },
        xaxis: {
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
        ]
    });
}

function showEventInfo (event) {
    event.target.style.transform = '';
    event.target.style.position = 'relative';
    event.target.removeAttribute('data-x')
    event.target.removeAttribute('data-y')
}

function updatePlotSource(name, color, id){
    $.ajax({
        processData: false,
        type: 'put',
        contentType: 'application/json',
        url: window.location.pathname + '/' + id,
        data: JSON.stringify({
            name: name,
            color: color
        })
    }).done(function (){
        let update = {
            'hovertemplate': `${name}, %{customdata}<extra></extra>`,
            'line.color': color
        }

        if (currentPanel[id] !== undefined)
            Plotly.restyle('multichart', update, currentPanel[id].id)

        currentPanel[id].updateTimeout = undefined;
    }).fail(function (response){
        showToast(response.responseText)
    })
}

function getRandomColor() {
    return '#' + (0x1000000 + Math.floor(Math.random() * 0x1000000)).toString(16).slice(1);
}

function dragMoveListener (event) {
    var target = event.target

    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)
}

function dragMoveListenerPanel (event) {
    var target = event.target.parentNode.parentElement;

    var x = (parseFloat(target.getAttribute('data-x')) || 0) + event.dx
    var y = (parseFloat(target.getAttribute('data-y')) || 0) + event.dy

    target.style.transform = 'translate(' + x + 'px, ' + y + 'px)'

    target.setAttribute('data-x', x)
    target.setAttribute('data-y', y)
}