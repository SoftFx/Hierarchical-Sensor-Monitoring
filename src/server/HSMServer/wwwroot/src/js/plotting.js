window.displayGraph = function(graphData, graphType, graphElementId, graphName) {
    let convertedData = convertToGraphData(graphData, graphType, graphName);
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
            getAddPlotButton(serviceButtonName, true, plotIcon, graphElementId, graphName),
            getAddPlotButton(heartBeatButtonName, false, plotIcon, graphElementId, graphName),
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

    if (graphType === "9")
    {
        let layout = getEnumLayout();
        layout.autosize = true;
        let heat = getHeatMapForEnum(convertedData[0])
        Plotly.newPlot(graphElementId, [heat], layout, config);
    }
    else if (graphType === "7") {
        let layout = getTimeSpanLayout(convertedData[0].y)
        layout.autosize = true;
        Plotly.newPlot(graphElementId, convertedData, layout, config);
    }
    else {
        if (zoomData === undefined || zoomData === null) {
            let layout = { autosize: true};
            if (graphType === "0")
                layout.yaxis = { 
                    tickmode: 'auto',
                    tick0: 0,
                    dtick: 1, 
                    nticks: 2
                };
            
            Plotly.newPlot(graphElementId, convertedData, layout, config);
        }
        else {
            let layout = createLayoutFromZoomData(zoomData);
            if (graphType === "0")
                layout.yaxis = {
                    tickmode: 'auto',
                    tick0: 0,
                    dtick: 1,
                    nticks: 2
                };
            
            Plotly.newPlot(graphElementId, convertedData, layout, config);
        }
    }
    let graphDiv = document.getElementById(graphElementId);
    graphDiv.on('plotly_relayout',
        function(eventData) {
            window.sessionStorage.setItem(graphElementId, JSON.stringify(eventData));
        });
}

function createLayoutFromZoomData(zoomData) {
    let processedData = Object.values(JSON.parse(zoomData));

    var layout = {
        xaxis : {
            range: [processedData[0], processedData[1]]
        },
        yaxis : {
            range: [processedData[2], processedData[3]]
        },
        autosize: true
    };
    return layout;
}

function getPreviousZoomData(graphElementId) {
    return window.sessionStorage.getItem(graphElementId);
}

function convertToGraphData(graphData, graphType, graphName) {
    let escapedData = JSON.parse(graphData);
    
    let data;
    let timeList;
    let uniqueData;
    switch (graphType) {
        case "0":
            uniqueData = getUniqueData(escapedData)
            data = getBoolData(uniqueData);
            timeList = getTimeList(uniqueData);
            return getBoolGraphData(timeList, data);
        case "1":
            data = getNumbersData(escapedData);
            timeList = getTimeList(escapedData);
            return getIntGraphData(timeList, data);
        case "2":
            data = getNumbersData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "scatter");
        case "4":
            return createBarGraphData(escapedData, graphName);
        case "5":
            return createBarGraphData(escapedData, graphName);
        case "7":
            uniqueData = getUniqueData(escapedData)
            escapedData.forEach(x => {
                uniqueData.forEach(y => {
                    if (x.time === y.time && ((new Date(x.receivingTime)) > (new Date(y.receivingTime)))) {
                        y.comment = x.comment;
                        y.receivingTime = x.receivingTime;
                        y.status = x.status;
                        y.time = x.time;
                        y.value = x.value;
                    }
                })
            });
            
            timeList = getTimeList(uniqueData);
            data = uniqueData.map(function (i) {
                let time = i.value.split(':');
                let temp = time[0].split('.')
                let days, hours, minutes,seconds;
                if (temp.length > 1) {
                    days = Number(temp[0]);
                    hours = Number(temp[1]);
                }
                else {
                    hours = Number(time[0]);
                    days = 0;
                }
                minutes = Number(time[1]);
                seconds = Number(time[2]);
               
                return new TimeSpan.TimeSpan(0, seconds, minutes, hours, days).totalMilliseconds();
            })
            
            return getTimeSpanGraphData(timeList, data, "lines");
        case "9":
            data = getNumbersData(escapedData)
            timeList = getTimeList(escapedData)
            return getEnumGraphData(timeList, data)
        default:
            return undefined;
    }
}

//Boolean 
{
    function getBoolData(escapedItems) {
        let bools = escapedItems.map(function (i) {
            let currentBoolean = i.value === true;
            return currentBoolean ? 1 : 0;
        });

        return bools;
    }
    
    function getUniqueData(data){
        return [...new Map(data.map(item => [item['time'], item])).values()]
    }
}

//Simple plots: integer, double and bool
{
    function getIntGraphData(timeList, dataList) {
        let data = [
            {
                x: timeList,
                y: dataList,
                mode: 'lines+markers',
                line: {
                        shape: 'hv'
                    },
                type: 'scatter'
            }
        ];
        return data;
    }
    
    function getBoolGraphData(timeList, dataList){
        return [
            {
                x: timeList,
                y: dataList.map((i) => {
                    return i === 1 ? 1 : 0;
                }),
                type: 'scatter',
                mode: 'markers',
                marker: {
                    color: dataList.map((i) => {
                        return i === 1 ? 'rgb(0,0,255)' : 'rgb(255,0,0)';
                    }),
                    size: 10
                },
                customdata: dataList.map((i) => {
                    return i === 1;
                }),
                hovertemplate: "%{x}, %{customdata}" +
                               "<extra></extra>"
            }
        ];
    }
    
    function getSimpleGraphData(timeList, dataList, chartType) {
        let data = [
            {
                x: timeList,
                y: dataList,
                type: chartType,
                //mode: "lines"
            }
        ];
        return data;
    }

    function getNumbersData(escapedItems) {
        let numbers = escapedItems.map(function (i) {
            return i.value;
        });

        return numbers;
    }

    function getTimeList(escapedItems) {
        return escapedItems.map(function (i) {
            return i.time;
        });
    }
}

function getTimeSpanGraphData(timeList, dataList, chartType){
    return [
        {
            x: timeList,
            y: dataList,
            type: chartType,
            customdata: getTextValForTimeSpan(dataList),
            hovertemplate: '%{customdata}<extra></extra>'
        }
    ];
}

function getTextValForTimeSpan(data){
    return data.map(function (i){
        const timespan = window.TimeSpan.fromMilliseconds(i)

        if (timespan === undefined) 
            return '0h 0m 0s';

        let text = `${timespan.hours}h ${timespan.minutes}m ${timespan.seconds}s`;

        if(timespan.days !== 0){
            return `${timespan.days}d ` + text;
        }
        return text
    })
}

function getTimeSpanLayout(datalist) {
    const MAX_TIME_POINTS = 10
   
    let maxVal = Math.max(...datalist)
    let step = Math.max(maxVal / MAX_TIME_POINTS, 1);
    let tVals = []
    let cur = 0
    while (cur <= maxVal) {
        tVals.push(cur);
        cur += step;
    }
    
    let tText =  getTextValForTimeSpan(tVals);
    
    return {
        yaxis: {
            ticktext: tText,
            tickvals: tVals,
            tickfont: {
                size: 10
            },
            automargin: "width+height"
        }
    };
}

//Boxplots
{
    function getTimeFromBars(escapedBarsData) {
        return escapedBarsData.map(function (d) {
            if (d.closeTime.toString().startsWith("0001")) {
                return d.openTime;
            }
            return d.closeTime;
        });
    }

    function createBarGraphData(escapedBarsData, graphName) {
        let max = getBarsMax(escapedBarsData);
        let min = getBarsMin(escapedBarsData);
        let median = getBarsMedian(escapedBarsData);
        let q1 = getBarsQ1(escapedBarsData);
        let q3 = getBarsQ3(escapedBarsData);
        let mean = getBarsMean(escapedBarsData);
        let timeList = getTimeFromBars(escapedBarsData);

        let data =
        [
            {
                "type": "box",
                "name": graphName,
                "q1": q1,
                "median": median,
                "q3": q3,
                "mean": mean,
                "lowerfence": min,
                "upperfence": max,
                "x": timeList
            }
        ];

        return data;
    }

    // Get numeric characteristics
    {
        function getBarsMin(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.min;
            });
        }

        function getBarsMax(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.max;
            });
        }

        function getBarsMedian(escapedBarsData) {
            let medians = new Array();

            escapedBarsData.map(function (d) {
                medians.push(d.percentiles[0.5]);
            });

            return medians;
        }

        function getBarsQ1(escapedBarsData) {
            let q1s = new Array();

            escapedBarsData.map(function (d) {
                q1s.push(d.percentiles[0.25]);
            });

            return q1s;
        }

        function getBarsQ3(escapedBarsData) {
            let q3s = new Array();

            escapedBarsData.map(function (d) {
                q3s.push(d.percentiles[0.75]);
            });

            return q3s;
        }

        function getBarsMean(escapedBarsData) {
            return escapedBarsData.map(function (d) {
                return d.mean;
            });
        }
    }
}


// plot type
function getPlotType(graphType) {
    // Use simple time series plot to display 
    if (graphType === 1 || graphType === 2) {
        return "scatter";
    }

    // Use box plot for box plots
    if (graphType === 4 || graphType === 5) {
        return "box";
    }

    // no plots for other types yet
    return undefined;
}

// Enum plot
{
    function getAddPlotButton(name, isStatusService, icon, graphElementId, graphName){
        return {
            name: name, //changing name doesn't work
            icon: icon,
            click: function(gd) {
                let graph = $(`#${graphElementId}`)[0];
                let graphLength = graph._fullData.length;
                if (graphLength > 1) {
                    Plotly.deleteTraces(graphElementId, graphLength - 1);
                    Plotly.update(graphElementId, {}, {hovermode: 'closest'});
                }
                else {
                    const { from, to } = getFromAndTo(graphName);
                    let body = Data(to, from, 1, graphName)
                    $.ajax({
                        type: 'POST',
                        data: JSON.stringify(body),
                        url: `SensorHistory/GetServiceStatusHistory?isStatusService=${isStatusService}`,
                        contentType: 'application/json',
                        dataType: 'html',
                        cache: false,
                        async: true,
                        success: function (data){
                            let escapedData = JSON.parse(data);
                            let graphData = getEnumGraphData(getTimeList(escapedData), getNumbersData(escapedData), isStatusService)
                            let ranges = graph._fullLayout.yaxis.range;
                            let heat = getHeatMapForEnum(graphData[0], ranges[0], ranges[1])
            
                            Plotly.addTraces(graphElementId, [heat]);
                            Plotly.update(graphElementId, {}, {hovermode: 'x'});
                        }
                    })
                }
            }
        }
    }
    
    function getHeatMapForEnum(data, minValue = 0, maxValue = 1) {
        return {
            x: data.x,
            y: [minValue, maxValue],
            z: [data.z],
            colorscale: [[0, '#FF0000'], [0.5, '#00FF00'], [1, 'blue']],
            zmin: 0,
            zmax: 1,
            showscale: false,
            type: 'heatmap',
            opacity: 0.25,
            customdata: [data.customdata],
            hovertemplate: '%{customdata}<extra></extra>',
        }
    }

    function getEnumGraphData(timeList, dataList, isServiceStatus = true){
        function getMappedData(data, time) {
            let customdata = [];
            let z = [];
            for (let i = 0; i < data.length; i++){
                if (isServiceStatus){
                    customdata.push(`${ServiceStatus[`${data[i]}`][1]} <br> ${new Date(time[i]).toUTCString()} - ${i + 1 >= data.length ? 'now' : new Date(time[i + 1]).toUTCString()}`)
                    z.push(ServiceStatus[`${data[i]}`][0] === ServiceStatus["4"][0] ? 0.5 : 0)
                }
                else {
                    customdata.push(`${data[i] === true ? ServiceStatus["4"][1] : ServiceStatus["1"][1]} <br> ${new Date(time[i]).toUTCString()} - ${i + 1 >= data.length ? 'now' : new Date(time[i + 1]).toUTCString()}`)
                    z.push(data[i] === true ? 0.5 : 0);
                }

            }
            
            return {
                customdata: customdata,
                z: z
            }
        }

        let currDate = new Date(new Date(Date.now()).toUTCString()).toISOString()
        timeList.push(currDate);
        let mappedData = getMappedData(dataList, timeList);

        return [
            {
                x: timeList,
                z: mappedData.z,
                customdata: mappedData.customdata,
                hovertemplate: '%{customdata}<extra></extra>',
            }
        ];
    }
    
    function  getEnumLayout() {
        return {
            yaxis: {
                visible: false,
            },
            automargin: "width+height",
        }
    }
    
    const ServiceStatus = {
        1 : ['#FF0000', 'Stopped'],
        2 : ['#BFFFBF', 'Start Pending'],
        3 : ['#FD6464','Stop Pending' ],
        4 : ['#00FF00', 'Running'],
        5 : ['#FFB403', 'Continue Pending'],
        6 : ['#809EFF', 'Pause Pending'],
        7 : ['#0314FF', 'Paused'],
        0 : ['#000000', 'Unknown']
    }
}