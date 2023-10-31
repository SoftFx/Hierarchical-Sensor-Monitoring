import {convertToGraphData} from "./plotting";

export function getPlotSourceView(id) {
    return new Promise(function (resolve, reject) {
        $.ajax({
            type: 'GET',
            url: sourceLink + `?sourceId=${id}`
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
                    let text = `<li id=${'source_'+ event.relatedTarget.id} class="d-flex list-group-item align-items-center justify-content-between">
                                    <div class="d-flex mx-1 align-items-center">
                                        <span>${data.name + data.units}</span>
                                        <input id=${'color_'+ event.relatedTarget.id} type="color" value=${color} class="form-control form-control-color mx-1">Plot color</input>
                                    </div>
                                    <button id=${'deletePlot_'+ event.relatedTarget.id} class="btn" type="button">
                                        <i class="fa-solid fa-xmark"></i>
                                    </button>
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
            }
        })
}

window.updateColor = function (color, id) {
    if (currentPanel[id] === undefined)
        return;

    if (currentPanel[id].colorTimeout !== undefined)
        clearTimeout(currentPanel[id].colorTimeout);

    currentPanel[id].colorTimeout = setTimeout(updatePlotColor, plotColorDelay, color, id);
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

function updatePlotColor(color, id) {
    let update = {
        'line.color': color
    }

    if (currentPanel[id] !== undefined)
        Plotly.restyle('multichart', update, currentPanel[id].id)

    currentPanel[id].colorTimeout = undefined;
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