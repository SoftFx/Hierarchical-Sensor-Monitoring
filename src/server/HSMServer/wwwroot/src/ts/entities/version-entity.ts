export interface IVersionEntity {
    major: number;
    minor: number;
    build: number;
    revision: number;
    majorRevision: number;
    minorRevision: number;
}

export interface IVersionValue {
    time: Date;
    tooltip: string;
    value: IVersionEntity;
}