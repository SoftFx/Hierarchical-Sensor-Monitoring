import {ServiceStatus} from "./plotting";

export class Plots {
    constructor(data) {
        this.basicInit();
    }

    basicInit() {
        this.x = [];
        this.y = [];
        this.customdata = [];
        this.type = '';
        this.mode = '';
        this.customdata = [];
        this.hovertemplate = '';
        this.showlegend = false;
    }

    setUpData(data) {
    }

    getPlotData() {
        return [this];
    }

    getLayout() {
        return {
            autosize: true
        }
    }
}

export class BoolPlot extends Plots {
    constructor(data) {
        super();
        this.type = 'scatter';
        this.mode = 'markers';
        this.marker = {
            color: [],
            size: 10
        };

        this.setUpData([...new Map(data.map(item => [item['time'], item])).values()]);
    }

    setUpData(data) {
        for (let i of data) {
            this.x.push(i.time)

            if (i.isTimeout === true) {
                this.y.push(-1)
            } else {
                this.y.push(i.value === true ? 1 : 0)
                this.customdata.push(i.value === true);
                this.marker.color.push(i.value === true ? 'rgb(0,0,255)' : 'rgb(255,0,0)');
            }
        }

        this.hovertemplate = "%{x}, %{customdata}" +
            "<extra></extra>";
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
}

export class IntegerPlot extends Plots {
    constructor(data) {
        super();

        this.type = 'scatter';
        this.mode = 'lines+markers';
        this.line = {
            shape: 'hv'
        }

        this.setUpData(data);
    }

    setUpData(data) {
        for (let i of data) {
            this.x.push(i.time)
            this.y.push(i.value)
        }
    }
}

export class DoublePlot extends Plots {
    constructor(data, name) {
        super();

        this.type = 'scatter';
        this.name = name;

        this.setUpData(data);
    }

    setUpData(data) {
        for (let i of data) {
            this.x.push(i.time)
            this.y.push(i.value)
        }
    }
    
    customSetUp(timelist, datalist) {
        this.x = timelist;
        this.y = datalist;
        
        return this;
    }
}

export class BarPLot extends Plots {
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
        window.barGraphData.count = this.count;
        window.barGraphData.min = this.lowerfence;
        window.barGraphData.max = this.upperfence;
        window.barGraphData.mean = this.mean;
        window.barGraphData.bar = [{
            "type": "box",
            "name": 'bar',
            "q1": this.q1,
            "median": this.median,
            "q3": this.q3,
            "mean": this.mean,
            "lowerfence": this.lowerfence,
            "upperfence": this.upperfence,
            "x": this.x,
            showlegend: false
        }];
        window.barGraphData.x = this.x;
    }
}

export class TimeSpanPlot extends Plots {
    constructor(data) {
        super();

        this.type = 'lines';
        this.customdata = [];

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
            this.customdata.push(this.getTimeSpanCustomData(timespan))
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

    getTimeSpanCustomData(timespan) {
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

export class EnumPlot extends Plots {
    constructor(data, isServiceStatus) {
        super();

        this.z = [];
        this.customdata = [];
        this.isServiceStatus = isServiceStatus;
        this.hovertemplate = '%{customdata}<extra></extra>';
        this.colorscale = [[0, '#FF0000'], [0.5, '#00FF00'], [1, 'blue']];
        this.zmin = 0;
        this.zmax = 1;
        this.showscale = false;
        this.type = 'heatmap';
        this.opacity = 0.25;
        this.setUpData(data)
    }

    setUpData(data) {
        let currDate = new Date(new Date(Date.now()).toUTCString()).toISOString();
        data.push({
            time: currDate
        })
        for (let i = 0; i < data.length - 1; i++) {
            this.x.push(data[i].time);
            if (this.isServiceStatus) {
                this.customdata.push(`${ServiceStatus[`${data[i].value}`][1]} <br> ${new Date(data[i].time).toUTCString()} - ${i + 1 >= data.length ? 'now' : new Date(data[i + 1].time).toUTCString()}`)
                this.z.push(ServiceStatus[`${data[i].value}`][0] === ServiceStatus["4"][0] ? 0.5 : 0)
            } else {
                this.customdata.push(`${data[i].value === true ? ServiceStatus["4"][1] : ServiceStatus["1"][1]} <br> ${new Date(data[i].time).toUTCString()} - ${i + 1 >= data.length ? 'now' : new Date(data[i + 1].time).toUTCString()}`)
                this.z.push(data[i].value === true ? 0.5 : 0);
            }
        }
        this.x.push(currDate);
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
            autosize: true
        }
    }
}