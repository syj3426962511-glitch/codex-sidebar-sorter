export async function desktopRequest(page, method, params) {
  return page.evaluate(async ({method, params}) => {
    const id = 'sorter-cloud-' + crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const listener = event => {
        const response = event.data?.type === 'mcp-response' ? event.data.message : null;
        if (response?.id !== id) return;
        clearTimeout(timer);
        window.removeEventListener('message', listener);
        response.error ? reject(new Error(JSON.stringify(response.error))) : resolve(response.result);
      };
      const timer = setTimeout(() => {
        window.removeEventListener('message', listener);
        reject(new Error('Desktop request timed out for ' + method));
      }, 30000);
      window.addEventListener('message', listener);
      window.electronBridge.sendMessageFromView({
        type:'mcp-request', hostId:'local', request:{id, method, params},
        priority:'interactive', source:'sidebar-sorter-cloud-acceptance', timeoutMs:25000,
      }).catch(error => {
        clearTimeout(timer);
        window.removeEventListener('message', listener);
        reject(error);
      });
    });
  }, {method, params});
}
