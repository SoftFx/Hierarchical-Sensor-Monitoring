export interface TestConfig {
  apiUrl: string;
  URLforAPI: string;
  apiUrl2: string;
  admin_user: string;
  admin_user_password: string;
  viewer_user: string;
  viewer_user_password: string;
  viewer_user_password_permanent: string;
  userName1: string;
  user1password: string;
  userName2: string;
  user2password: string;

  folder_name: string;
  folder_description: string;
  folder_color: string;

  folder_name1: string;
  folder_description1: string;
  folder_color1: string;

  folder_name2: string;
  folder_description2: string;
  folder_color2: string;

  folder_name3: string;
  folder_description3: string;
  folder_color3: string

}

// Base URL is taken from the CI env (PLAYWRIGHT_TEST_BASE_URL, set by the workflow) and falls back to
// the local Docker server, so the same suite runs in CI and locally without editing this file.
const baseURL = process.env.PLAYWRIGHT_TEST_BASE_URL || 'https://localhost:44333';

export const testConfig: TestConfig = {
  URLforAPI: `${baseURL}/Home`,
  apiUrl: `${baseURL}/Account/Index?ReturnUrl=%2FHome`,
  apiUrl2: `${baseURL}/Home`,

  // NOTE: test-only credentials — NOT real secrets. `default`/`default` is the admin the Docker test
  // container seeds; the test-user passwords below exist solely for these E2E runs.
  admin_user: 'default',
  admin_user_password: 'default',
  userName1:'test_user1',
  user1password: '1234567890',
  userName2: 'test_user2',
  user2password: '0987654321',
  viewer_user: 'maryia.pazniak.viewer',
  viewer_user_password: '12345678',
  viewer_user_password_permanent: '11111111',
  
  // Data for create Folder
  folder_name: 'TestAutoFolder',
  folder_description: 'test delete',
  folder_color: '#0f4daa',
   
  folder_name1: 'Folder1',
  folder_description1: 'create folder1',
  folder_color1: '#8f1d69',

  folder_name3: 'Folder2',
  folder_description3: 'create folder1',
  folder_color3: '#39de34',
  
  //Data for modify folder general settings
  folder_name2: 'TestAutoFolder2',
  folder_description2: 'test delete2',
  folder_color2: '#a11b2f',
};

export const testData = {
  templatePath: 'BetaTTS/BetaTTS/UpdateService/.module/Service alive',
  duplicateError: 'The name must be unique.',
  templateName: 'Test',
  templateName2: 'Test2',
  testFolderName: '_1TestFolder',
  testFolderDescription: 'Test folder for alert template',
  testFolderColor: '#ff5733',
  testTemplatePath: 'aaa/bbb',
};

