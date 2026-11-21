window.pdfnetDownload = async (fileName, streamReference) => {
    const data = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([data], { type: "application/pdf" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
