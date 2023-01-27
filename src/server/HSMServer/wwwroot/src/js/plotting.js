window.displayGraph = function(graphData, graphType, graphElementId, graphName) {
    let convertedData = convertToGraphData(graphData, graphType, graphName);

    console.log('converted graph data:', convertedData);
    let zoomData = getPreviousZoomData(graphElementId);
    var config = { responsive: true }
    if(graphType === "7"){
        convertedData[1].autosize = true;
        if (zoomData === undefined || zoomData === null) {
            Plotly.newPlot(graphElementId, convertedData[0], convertedData[1], config);
        } else {
            let processedData = Object.values(JSON.parse(zoomData));
            console.log('zoomData:', zoomData);
            console.log('processedData:', processedData);
            // if(processedData.length >= 2){
            //     convertedData[1].xaxis.range = [processedData[1], processedData[0]];
            //     //convertedData[1].yaxis.range =  [processedData[2], processedData[3]];
            // }
            Plotly.newPlot(graphElementId, convertedData[0], convertedData[1], config);
        }
        //
        // var graph = document.getElementById(graphElementId)
        // let ticks = graph.querySelectorAll('.ytick')
        // ticks.forEach((tick) => {
        //    let text = Number(tick.querySelector('text').innerHTML);
        //    console.log(text);
        //   
        //    let newText = new TimeSpan.TimeSpan();
        //    newText.addSeconds(text);
        //    let newFormat = `${newText.days}d ${newText.hours}h ${newText.minutes}m ${newText.seconds}s`
        //    console.log(newText.days, newText.hours, newText.minutes, newText.seconds);
        //    tick.querySelector('text').innerHTML = newFormat;
        // })
        // console.log(ticks)
        
    }else{
        if (zoomData === undefined || zoomData === null) {
            var layout = { autosize: true };
            Plotly.newPlot(graphElementId, convertedData, layout, config);
        } else {
            let layout = createLayoutFromZoomData(zoomData);
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
    console.log("processedData:", processedData)
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
            return createBarGraphData(escapedData, graphName);
        case "5":
            return createBarGraphData(escapedData, graphName);
        case "7":
            console.log("escapedData:", escapedData)
            uniqueData = getUniqueData(escapedData)
            console.log("uniqueData:", uniqueData)
            escapedData.forEach(x => {
                uniqueData.forEach(y => {
                    if(x.time === y.time && (new Date(x.receivingTime)) >= (new Date(y.receivingTime))){
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
                if(temp.length > 1){
                    days = Number(temp[0]);
                    hours = Number(temp[1]);
                }else{
                    hours = Number(time[0]);
                    days = 0;
                }
                minutes = Number(time[1]);
                seconds = Number(time[2]);
               
                // let t = new Date(1970, 0, 1); // Epoch
                // t.setSeconds(new TimeSpan.TimeSpan(0, seconds, minutes, hours, days).totalSeconds());
                // return t;
                return new TimeSpan.TimeSpan(0, seconds, minutes, hours, days).totalSeconds();
            })
            let dataText = data.map(function (i){
                const timespan = TimeSpan.fromSeconds(i)
                let text = ``;
                if(timespan.days !== 0){
                    text += `${timespan.days}d `
                }
                text += `${timespan.hours}h ${timespan.minutes}m ${timespan.seconds}s`
                return text
            })
            let minVal = Math.min(...data);
            let maxVal = Math.max(...data);
            let diff =  minVal;
            console.log(diff, minVal, maxVal)
            const timespan = TimeSpan.fromSeconds(diff)
            let array = []
            diff += minVal;
            while (diff < maxVal)
            {
                console.log(diff)
                array.push(diff);
                diff += minVal
            }
            console.log(array)
            let tVals = [minVal, ...array ,maxVal];
            let tText = tVals.map(function (i){
                const timespan = TimeSpan.fromSeconds(i)
                let text = ``;
                if(timespan.days !== 0){
                    text += `${timespan.days}d `
                }
                text += `${timespan.hours}h ${timespan.minutes}m ${timespan.seconds}s`
                return text
                })
            const layout ={
                yaxis: {
                    ticktext: tText,
                    tickvals: tVals,
                    tickfont:{
                        size: 8
                    },
                    //dtick: 60*60*24
                }
            };
            return getTimeSpanGraphData(timeList, data, "bar", layout)
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
        return [...new Map(data.map(item => [item['time'] ,item])).values()]
    }
}

//Simple plots: integer and double
{
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
    
    function getTimeSpanGraphData(timeList, dataList, chartType, layout){
        let cData = dataList.map(function (i){
            const timespan = TimeSpan.fromSeconds(i)
            let text = ``;
            if(timespan.days !== 0){
                text += `${timespan.days}d `
            }
            text += `${timespan.hours}h ${timespan.minutes}m ${timespan.seconds}s`
            return text
        })
        let data = [
            {
                x: timeList,
                y: dataList,
                type: chartType,
                customdata: cData,
                hovertemplate: '%{customdata}<extra></extra>'
                //mode: "lines"
            }
        ];
        return [data, layout];
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