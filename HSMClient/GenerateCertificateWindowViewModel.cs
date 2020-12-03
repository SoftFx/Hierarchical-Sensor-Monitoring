using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using HSMClientWPFControls.Bases;

namespace HSMClient
{
    public class GenerateCertificateWindowViewModel : ViewModelBase, IDataErrorInfo
    {
        private string _countryName;
        private string _stateOrProvinceName;
        private string _localityName;
        private string _organizationName;
        private string _organizationUnitName;
        private string _commonName;
        private string _emailAddress;
        public GenerateCertificateWindowViewModel()
        {

        }

        public string CountryName
        {
            get => _countryName;
            set
            {
                _countryName = value;
                OnPropertyChanged(nameof(CountryName));
            }
        }

        public string StateOrProvinceName
        {
            get => _stateOrProvinceName;
            set
            {
                _stateOrProvinceName = value;
                OnPropertyChanged(nameof(StateOrProvinceName));
            }
        }

        public string LocalityName
        {
            get => _localityName;
            set
            {
                _localityName = value;
                OnPropertyChanged(nameof(LocalityName));
            }
        }

        public string OrganizationName
        {
            get => _organizationName;
            set
            {
                _organizationName = value;
                OnPropertyChanged(nameof(OrganizationName));
            }
        }

        public string OrganizationUnitName
        {
            get => _organizationUnitName;
            set
            {
                _organizationUnitName = value;
                OnPropertyChanged(nameof(OrganizationUnitName));
            }
        }

        public string CommonName
        {
            get => _commonName;
            set
            {
                _commonName = value;
                OnPropertyChanged(nameof(CommonName));
            }
        }

        public string EmailAddress
        {
            get => _emailAddress;
            set
            {
                _emailAddress = value;
                OnPropertyChanged(nameof(EmailAddress));
            }
        }

        public string Error { get; }

        public string this[string columnName]
        {
            get
            {
                string error = string.Empty;
                switch (columnName)
                {
                    case "CountryName":
                    {
                        if (string.IsNullOrEmpty(CountryName))
                        {
                            error = "You must enter the country name!";
                        }

                        if (CountryName.Length > 2)
                        {
                            error = "Country name must not be longer than 2 symbols!";
                        }
                        break;
                    }
                    case "OrganizationName":
                    {
                        if (string.IsNullOrEmpty(OrganizationName))
                        {
                            error = "You must specify organization name!";
                        }

                        break;
                    }
                }
                return error;
            }
        }
    }
}
