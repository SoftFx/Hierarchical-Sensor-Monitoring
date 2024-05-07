export interface VersionEntity {
    major: number;
    minor: number;
    build: number;
    revision: number;
    majorRevision: number;
    minorRevision: number;
}

export interface VersionValue {
    time: Date;
    tooltip: string;
    value: VersionEntity;
}