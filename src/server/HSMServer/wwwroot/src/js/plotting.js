window.displayGraph = function(graphData, graphType, graphElementId, graphName) {
    let convertedData = convertToGraphData(graphData, graphType, graphName);
    let zoomData = getPreviousZoomData(graphElementId);
    var config = { responsive: true }
    if (graphType === "7") {
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
            
            return getTimeSpanGraphData(timeList, data, "lines")
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
                        shape: 'vh'
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