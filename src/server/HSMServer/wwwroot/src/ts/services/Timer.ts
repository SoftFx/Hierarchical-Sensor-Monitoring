export class Timer {
    startTime: Date;
    stopTime: Date;
    duration: number;
    
    
    start(){
        this.startTime = new Date(Date.now());
    }
    
    
    stop() {
        this.stopTime = new Date(Date.now());
        this.duration = this.stopTime.getTime() - this.startTime.getTime();
    }
}