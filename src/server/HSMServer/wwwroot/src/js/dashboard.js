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
                                                <input id=${'name_input_' + data.id} class="form-control"  value="${data.label}" type="text" style="flex-grow: 10"></input>
                                                <input id=${'color_' + data.id} type="color" value=${data.color} class="form-control form-control-color mx-1 ="></input>
                                            </div>
                                            <div class="d-flex align-items-center">
                                                <span id=${'redirectToHome_' + data.id} class="ms-1 redirectToHome" style="color: grey;font-size: x-small;text-decoration-line: underline;cursor: pointer;">
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
    const interact = window.interact('.resize-draggable')
    addDraggable(interact)
    addResizable(interact)
    for (let i in currentPanel){
        currentPanel[i].requestTimeout = setInterval(function() {
            $.ajax({
                type: 'get',
                url: window.location.pathname + '/SourceUpdate' + `/${currentPanel[i].panelId}/${i}`,
            }).done(function(data){
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
    interact('.resize-draggable').options.drag.enabled = false;
}

window.enableDragAndResize = function () {
    interact('.resize-draggable').options.resize.enabled = true;
    interact('.resize-draggable').options.drag.enabled = true;
}

function addDraggable(interactable) {
    interactable.draggable({
        inertia: true,
        modifiers: [
            interact.modifiers.restrictRect({
                restriction: 'parent',
                endOnly: true
            })
        ],
        autoScroll: true,

        listeners: {
            move: dragMoveListener,

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
                min: { width: 100, height: 50 }
            })
        ],

        inertia: true
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
        console.log(item)
        if (currentPanel[item].id >= idToCompare)
            currentPanel[item].id = currentPanel[item].id - 1;
    }
}

window.initMultichart = function (chartId) {
    return Plotly.newPlot(chartId, [], {
        hovermode: 'x',
        dragmode: 'zoom',
        autosize: true,
        height: 300,
        margin: {
            autoexpand: true,
            l: 20,
            r: 10,
            t: 10,
            b: 40,
        },
        xaxis: {
            title: {
                text: 'Time',
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