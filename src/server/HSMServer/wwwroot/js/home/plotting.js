﻿function displayGraph(graphData, graphType, graphElementId, graphName) {
    let convertedData = convertToGraphData(graphData, graphType, graphName);

    console.log('converted graph data:', convertedData);
    let zoomData = getPreviousZoomData(graphElementId);
    if (zoomData === undefined || zoomData === null) {
        Plotly.newPlot(graphElementId, convertedData);    
    } else {
        let layout = createLayoutFromZoomData(zoomData);
        Plotly.newPlot(graphElementId, convertedData, layout);
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
        }
    };
    return layout;
}

function getPreviousZoomData(graphElementId) {
    return window.sessionStorage.getItem(graphElementId);
}

function convertToGraphData(graphData, graphType, graphName) {
    let escapedData = JSON.parse(graphData);

    let data;
    let deserialized;
    let timeList;
    switch (graphType) {
        case "0":
            data = getBoolData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "bar");
        case "1":
            data = getNumbersData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "scatter");
        case "2":
            data = getNumbersData(escapedData);
            timeList = getTimeList(escapedData);
            return getSimpleGraphData(timeList, data, "scatter");
        case "4":
            //deserialized = getDeserializedBarsData(escapedData);
            return createBarGraphData(escapedData, graphName);
        case "5":
            //deserialized = getDeserializedBarsData(escapedData);
            return createBarGraphData(escapedData, graphName);
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
}

//Simple plots: integer and double
{
    function getSimpleGraphData(timeList, dataList, chartType) {
        console.log(timeList);
        console.log(dataList);
        console.log(chartType);
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

//Boxplots
{

    //function getDeserializedBarsData(escapedItems) {
    //    let deserialized = escapedItems.map(function (i) {
    //        return JSON.parse(i.typedData);
    //    });

    //    return deserialized;
    //}

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
        //let median = getBarsMedian(escapedBarsData);
        //let q1 = getBarsQ1(escapedBarsData);
        //let q3 = getBarsQ3(escapedBarsData);
        let mean = getBarsMean(escapedBarsData);
        let timeList = getTimeFromBars(escapedBarsData);

        let data =
        [
            {
                "type": "box",
                "name": graphName,
                "q1": min,
                "median": mean,
                "q3": max,
                "mean": mean,
                "lowerfence": min,
                "upperfence": max,
                "x": timeList
            }
        ];

        console.log(data);

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

        //function getBarsMedian(escapedBarsData) {
        //    let medians = new Array();

        //    escapedBarsData.map(function (d) {
        //        d.Percentiles.filter(p => p.Percentile === 0.5).map(function (p) {
        //            medians.push(p.Value);
        //        });
        //    });

        //    return medians;
        //}

        //function getBarsQ1(escapedBarsData) {
        //    let q1s = new Array();

        //    escapedBarsData.map(function (d) {
        //        d.Percentiles.filter(p => p.Percentile === 0.25).map(function (p) {
        //            q1s.push(p.Value);
        //        });
        //    });

        //    return q1s;
        //}

        //function getBarsQ3(escapedBarsData) {
        //    let q3s = new Array();

        //    escapedBarsData.map(function (d) {
        //        d.Percentiles.filter(p => p.Percentile === 0.75).map(function (p) {
        //            q3s.push(p.Value);
        //        });
        //    });

        //    return q3s;
        //}

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