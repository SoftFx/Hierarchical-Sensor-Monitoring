﻿import {ServiceStatus} from "./plotting";

export const ServiceAliveIcon = {
    'width': 500,
    'height': 600,
    'path': 'M228.3 469.1L47.6 300.4c-4.2-3.9-8.2-8.1-11.9-12.4h87c22.6 0 43-13.6 51.7-34.5l10.5-25.2 49.3 109.5c3.8 8.5 12.1 14 21.4 14.1s17.8-5 22-13.3L320 253.7l1.7 3.4c9.5 19 28.9 31 50.1 31H476.3c-3.7 4.3-7.7 8.5-11.9 12.4L283.7 469.1c-7.5 7-17.4 10.9-27.7 10.9s-20.2-3.9-27.7-10.9zM503.7 240h-132c-3 0-5.8-1.7-7.2-4.4l-23.2-46.3c-4.1-8.1-12.4-13.3-21.5-13.3s-17.4 5.1-21.5 13.3l-41.4 82.8L205.9 158.2c-3.9-8.7-12.7-14.3-22.2-14.1s-18.1 5.9-21.8 14.8l-31.8 76.3c-1.2 3-4.2 4.9-7.4 4.9H16c-2.6 0-5 .4-7.3 1.1C3 225.2 0 208.2 0 190.9v-5.8c0-69.9 50.5-129.5 119.4-141C165 36.5 211.4 51.4 244 84l12 12 12-12c32.6-32.6 79-47.5 124.6-39.9C461.5 55.6 512 115.2 512 185.1v5.8c0 16.9-2.8 33.5-8.3 49.1z'
}

export const ServiceStatusIcon = {
    'width': 500,
    'height': 600,
    'path': 'M32 32c17.7 0 32 14.3 32 32V400c0 8.8 7.2 16 16 16H480c17.7 0 32 14.3 32 32s-14.3 32-32 32H80c-44.2 0-80-35.8-80-80V64C0 46.3 14.3 32 32 32zM160 224c17.7 0 32 14.3 32 32v64c0 17.7-14.3 32-32 32s-32-14.3-32-32V256c0-17.7 14.3-32 32-32zm128-64V320c0 17.7-14.3 32-32 32s-32-14.3-32-32V160c0-17.7 14.3-32 32-32s32 14.3 32 32zm64 32c17.7 0 32 14.3 32 32v96c0 17.7-14.3 32-32 32s-32-14.3-32-32V224c0-17.7 14.3-32 32-32zM480 96V320c0 17.7-14.3 32-32 32s-32-14.3-32-32V96c0-17.7 14.3-32 32-32s32 14.3 32 32z'
}

const Colors = {
    default: 'rgba(31, 119, 180, 1)',
    red: 'rgba(255,0,0,1)',
    TtlGrey: 'rgba(192,192,192,1)',
    blue: 'rgba(0,0,255,1)',
    line: 'rgb(231, 99, 250)'
}

const MarkerSize = {
    default: 0,
    defaultLineSize: 2,
    small: 5,
    Ttl: 10
}

export class Plot {
    x = [];
    y = [];
    customdata = [];
    type = '';
    mode = '';
    showlegend = false;
    hovertemplate = "%{x}, %{customdata}<extra></extra>";

    constructor(data) { }

    setUpData(data) {}

    getPlotData() {
        return [this];
    }

    getLayout() {
        return {
            autosize: true
        }
    }

    checkTtl(value) {
        return !!value.isTimeout;
    }

    addCustomData(value, compareFunc = null, customField = 'value') {
        if (this.checkTtl(value))
            this.customdata.push(value.comment);
        else
            this.customdata.push(compareFunc === null ? value[customField] : compareFunc(value));
    }

    markerColorCompareFunc(value) {
        if (this.checkTtl(value))
            return Colors.TtlGrey

        return Colors.default;
    }

    getMarkerSize (value) {
        if (this.checkTtl(value))
            return MarkerSize.Ttl;

        return MarkerSize.defaultLineSize;
    }
}

export class BoolPlot extends Plot {
    constructor(data) {
        super();
        this.type = 'scatter';
        this.mode = 'markers';
        this.marker = {
            color: [],
            size: [],
            opacity: 1,
            line: {
                color: Colors.line,
                width: 0
            }
        };
        this.setUpData([...new Map(data.map(item => [item['time'], item])).values()]);
    }

    setUpData(data) {
        for (let i of data) {
            this.x.push(i.time)

            this.y.push(i.value === true ? 1 : 0)
            this.addCustomData(i, this.customDataCompareFunc)
            this.marker.color.push(this.markerColorCompareFunc(i));
            this.marker.size.push(this.getMarkerSize(i))
        }

        this.hovertemplate = "%{x}, %{customdata}<extra></extra>";
    }

    customDataCompareFunc(value) {
        return value.value === true;
    }

    markerColorCompareFunc(value) {
        if (this.checkTtl(value))
            return Colors.TtlGrey;

        return this.customDataCompareFunc(value) ? Colors.blue : Colors.red;
    }

    getLayout() {
        return {
            autosize: true,
            yaxis: {
                tickmode: 'auto',
                tick0: 0,
                dtick: 1,
                nticks: 2
            }
        }
    }

    getMarkerSize(value) {
        if (this.checkTtl(value))
            return MarkerSize.Ttl;

        return MarkerSize.small;
    }
}

export class IntegerPlot extends Plot {
    constructor(data) {
        super();

        this.type = 'scatter';
        this.mode = 'lines+markers';
        this.line = {
            shape: 'hv'
        }
        this.marker = {
            color: [],
            size: [],
            opacity: 1,
            line: {
                color: Colors.line,
                width: 0
            }
        };
        this.setUpData(data);
    }

    setUpData(data) {
        for (let i of data) {
            this.x.push(i.time)
            this.y.push(i.value)
            this.addCustomData(i);
            this.marker.size.push(this.getMarkerSize(i));
            this.marker.color.push(this.markerColorCompareFunc(i));
        }
    }
}

export class DoublePlot extends Plot {
    constructor(data, name, field = 'value') {
        super();

        this.type = 'scatter';
        this.name = name;
        this.marker = {
            color: [],
            size: [],
            opacity: 1,
            line: {
                color: Colors.line,
                width: 0
            }
        };
        this.setUpData(data, field);
    }

    setUpData(data, customField = 'value') {
        for (let i of data) {
            this.x.push(i.time)
            this.y.push(i[customField])

            this.addCustomData(i, null, customField);
            this.marker.size.push(this.getMarkerSize(i));
            this.marker.color.push(this.markerColorCompareFunc(i));
        }

        if (customField !== 'value') {
            this.hovertemplate = `%{customdata} <extra>${this.name}</extra>`
        }
    }
}

export class BarPLot extends Plot {
    constructor(data, name) {
        super();

        this.type = 'box';
        this.name = 'bar';

        this.upperfence = [];
        this.lowerfence = [];
        this.median = [];
        this.q1 = [];
        this.q3 = [];
        this.mean = [];
        this.count = [];

        this.setUpData(data);
    }

    setUpData(data) {
        for (let i of data) {
            if (i.closeTime.toString().startsWith("0001"))
                this.x.push(i.openTime);
            else
                this.x.push(i.closeTime);

            this.upperfence.push(i.max);
            this.lowerfence.push(i.min);
            this.median.push(i.percentiles[0.5]);
            this.q1.push(i.percentiles[0.25]);
            this.q3.push(i.percentiles[0.75]);
            this.mean.push(i.mean);
            this.count.push(i.count);
        }

        window.barGraphData.plot = this;
        window.barGraphData.plotData = data;
    }
}

export class TimeSpanPlot extends Plot {
    constructor(data) {
        super();

        this.type = 'scatter';
        this.mode = 'lines+markers';
        this.customdata = [];
        this.marker = {
            color: [],
            size: [],
            opacity: 1,
            line: {
                color: Colors.line,
                width: 0
            }
        };
        this.setUpData(data)
    }

    setUpData(data) {
        let uniqueData = [...new Map(data.map(item => [item['time'], item])).values()];
        data.forEach(x => {
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

        for (let i of uniqueData) {
            this.x.push(i.time);

            let timespan = this.getTimeSpanValue(i);
            this.y.push(timespan.totalMilliseconds());
            this.customdata.push(this.getTimeSpanCustomData(timespan, i))
            this.marker.color.push(this.markerColorCompareFunc(i));
            this.marker.size.push(this.getMarkerSize(i));
        }

        this.hovertemplate = '%{customdata}<extra></extra>'
    }

    getTimeSpanValue(value) {
        let time = value.value.split(':');
        let temp = time[0].split('.')
        let days, hours, minutes, seconds;
        if (temp.length > 1) {
            days = Number(temp[0]);
            hours = Number(temp[1]);
        } else {
            hours = Number(time[0]);
            days = 0;
        }
        minutes = Number(time[1]);
        seconds = Number(time[2]);

        return new TimeSpan.TimeSpan(0, seconds, minutes, hours, days);
    }

    getTimeSpanCustomData(timespan, value) {
        if (this.checkTtl(value))
            return value.comment;

        if (timespan === undefined)
            return '0h 0m 0s';

        let text = `${timespan.hours}h ${timespan.minutes}m ${timespan.seconds}s`;

        return timespan.days !== 0 ? `${timespan.days}d ` + text : text;
    }

    getLayout() {
        const MAX_TIME_POINTS = 10

        let maxVal = Math.max(...this.y)
        let step = Math.max(maxVal / MAX_TIME_POINTS, 1);
        let tVals = []
        let cur = 0
        while (cur <= maxVal) {
            tVals.push(cur);
            cur += step;
        }

        return {
            yaxis: {
                ticktext: this.customdata,
                tickvals: tVals,
                tickfont: {
                    size: 10
                },
                automargin: "width+height"
            },
            autosize: true
        };
    }
}

export class EnumPlot extends Plot {
    constructor(data, isServiceStatus) {
        super();

        this.z = [];
        this.customdata = [];
        this.isServiceStatus = isServiceStatus;
        this.hovertemplate = '%{customdata}<extra></extra>';
        this.colorscale = [[0, '#FF0000'], [0.5, '#00FF00'], [1, 'grey']];
        this.zmin = 0;
        this.zmax = 1;
        this.showscale = false;
        this.type = 'heatmap';
        this.opacity = 0.25;
        this.setUpData(data);
    }

    setUpData(data) {
        let timeObject = {
            beginTime: "",
            endTime: "",

            data: data,

            getCustomString: function () {
                return `${this.beginTime} - ${this.endTime}`;
            },
            
            setUpTime: function (index){
                this.beginTime = new Date(this.data[index].time).toUTCString();

                if (this.data[index].lastReceivingTime !== null)
                    this.endTime = new Date(this.data[index].lastReceivingTime).toUTCString()
                else
                {
                    if (index + 1 < this.data.length)
                        this.endTime = new Date(this.data[index + 1].time).toUTCString()
                    else
                        this.endTime = 'now';
                }
            }
        }
        for (let i = 0; i < data.length; i++) {
            timeObject.setUpTime(i);

            this.x.push(data[i].lastReceivingTime ?? data[i].time);
            if (this.isServiceStatus) {
                this.customdata.push(`${ServiceStatus[`${data[i].value}`][1]} <br>`)
                this.z.push(ServiceStatus[`${data[i].value}`][0] === ServiceStatus["4"][0] ? 0.5 : 0)
            } else {
                if (this.checkTtl(data[i])) {
                    this.z.push(1);
                    this.customdata.push(`${ServiceStatus["8"][1]} <br>`)
                }
                else {
                    this.z.push(data[i].value === true ? 0.5 : 0);
                    this.customdata.push(`${data[i].value === true ? ServiceStatus["4"][1] : ServiceStatus["1"][1]} <br>`)
                }
            }
            
            this.customdata[this.customdata.length - 1] += timeObject.getCustomString();
        }
    }

    getPlotData(name = 'custom', minValue = 0, maxValue = 1) {
        this.y = [minValue, maxValue];
        this.z = [this.z];
        this.customdata = [this.customdata];
        this.name = name;

        return super.getPlotData();
    }

    getLayout() {
        return {
            yaxis: {
                visible: false,
            },
            automargin: "width+height",
            autosize: true,
        }
    }
    
    getTitle(path){
        return  {
            text: `Background path: ${path}`,
                font: {
                size: 12
            },
            yref: 'paper',
                xref: 'paper',
                automargin: true,
                xanchor: 'left',
                pad: {
                b: 10
            },
            x: 0
        }
    }
}
