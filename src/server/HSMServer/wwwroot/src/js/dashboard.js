import {convertToGraphData} from "./plotting";

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
        })
    })
}

export const currentPanel = {};
export const plotColorDelay = 1000;

export function Model(id) {
    this.id = id;
    this.colorTimeout = undefined;
    this.nameTimeout = undefined;
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
                (data) => {
                    let text = `<li id=${'source_' + event.relatedTarget.id} class="d-flex flex-wrap list-group-item my-1 align-items-center justify-content-between"
                                    style="border-top-width: 1px;
                                           border-radius: 5px;"
                                    >
                                    <div class="d-flex align-items-center justify-content-between w-100">
                                        <div class="d-flex mx-1 align-items-center" style="flex-grow: 10">
                                            <input id=${'name_input_' + event.relatedTarget.id} class="form-control"  value="${data.name}" type="text" style="flex-grow: 10"></input>
                                            <input id=${'color_' + event.relatedTarget.id} type="color" value=${color} class="form-control form-control-color mx-1 ="></input>
                                        </div>
                                        <div class="d-flex flex-grow-1"></div>
                                        <button id=${'deletePlot_' + event.relatedTarget.id} class="btn" type="button" style="color: red">
                                            <i class="fa-solid fa-xmark"></i>
                                        </button>
                                    </div>
     
                                    <div class="d-flex align-items-center">
                                         <a class="ms-1" href="" style="color: grey;font-size: x-small">
                                            ${data.path}
                                        </a>
                                    </div>
                                </li>`

                    let plot = convertToGraphData(JSON.stringify(data.values), data.sensorInfo, event.relatedTarget.id, color);
                    plot.id = event.relatedTarget.id
                    plot.name = data.name;
                    plot.mode = 'lines';
                    plot.hovertemplate = `${plot.name}, %{customdata}<extra></extra>`
                    Plotly.addTraces('multichart', plot.getPlotData());

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
                    Plotly.relayout('multichart', updateLayout)

                    sources.html(function(n, origText){
                        return origText + text;
                    });

                    currentPanel[event.relatedTarget.id] = new Model($('#multichart')[0].data.length - 1);
                },
                (error) => {
                    showToast(error)
                }
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

    window.interact('.resize-draggable')
        .draggable({
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
        .resizable({
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
                        height: event.rect.heigh
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

window.updateColor = function (color, id) {
    if (currentPanel[id] === undefined)
        return;

    if (currentPanel[id].colorTimeout !== undefined)
        clearTimeout(currentPanel[id].colorTimeout);

    currentPanel[id].colorTimeout = setTimeout(updatePlotColor, plotColorDelay, color, id);
}

window.updateName = function (name, id){
    if (currentPanel[id] === undefined)
        return;

    if (currentPanel[id].nameTimeout !== undefined)
        clearTimeout(currentPanel[id].nameTimeout);

    currentPanel[id].nameTimeout = setTimeout(updatePlotName, plotColorDelay, name, id);
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
    Plotly.newPlot(chartId, [], {
        hovermode: 'x',
        dragmode: 'zoom',
        autosize: true,
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
function updatePlotColor(color, id) {
    let update = {
        'line.color': color
    }

    if (currentPanel[id] !== undefined)
        Plotly.restyle('multichart', update, currentPanel[id].id)

    currentPanel[id].colorTimeout = undefined;
}

function updatePlotName(name, id) {
    let update = {
        'hovertemplate': `${name}, %{customdata}<extra></extra>` 
    }

    if (currentPanel[id] !== undefined)
        Plotly.restyle('multichart', update, currentPanel[id].id)

    currentPanel[id].nameTimeout = undefined;
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