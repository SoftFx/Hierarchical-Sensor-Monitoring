export function getPlotSourceView(id) {
    return new Promise(function (resolve, reject) {
        $.ajax({
            type: 'GET',
            url: sourceLink + `?id=${id}`
        }).done(function (data) {
            resolve(data);
        })
    })
}

export const currentDashboard = {};
export const plotColorDelay = 1000;

export function Model(id) {
    this.id = id;
    this.colorTimeout = undefined;
}

window.updateColor = function (color, id) {
    if (currentDashboard[id] === undefined)
        return;

    if (currentDashboard[id].colorTimeout !== undefined)
        clearTimeout(currentDashboard[id].colorTimeout);

    currentDashboard[id].colorTimeout = setTimeout(updatePlotColor, plotColorDelay, color, id);
}

function updatePlotColor(color, id) {
    let update = {
        'line.color': color
    }

    if (currentDashboard[id] !== undefined)
        Plotly.restyle('plot', update, currentDashboard[id].id)

    currentDashboard[id].colorTimeout = undefined;
}